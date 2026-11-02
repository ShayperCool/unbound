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

### User planning notes, 2026-10-31

- Humanoid probe sources: the user reports a local `~/Dungeon` project with many
  humanoid animations. Use it to select representative rigs and clips for retargeting,
  muscle curves, IK and `OnAnimatorIK`; root-motion bone selection, Bake Into Pose and
  `MatchTarget` need their own controls. The standard Unity Third Person sample is an
  alternative or additional comparison project. `~/Dungeon` was not present in
  this checkout environment when this note was recorded;
  resolve the source location on the machine holding the project before using it.
- Preferred owner for the remaining rendering work: Claude. Scope: shadowmask mixing,
  per-pixel LPPV, skinned motion vectors, halos/flares and the remaining low-level GPU
  APIs listed in `COMPATIBILITY_GAPS.md`. This records the user's assignment preference;
  it does not claim that a Claude task has started or that these features are complete.

For each selected feature, record independent agreeing Unity Editor references for the
explicit release before accepting an implementation. Keep source projects' authored files
intact, put captures and run artifacts in `Library/Unbound/` or the Unbound cache, and
commit only probe sources and the canonical release references under `tests/projects/`.

## Trail runtime and particle aging

Implemented BakeTrailsMesh, default/custom trail layouts, packed neighbor/percentage/width
streams, exact sampled heads/tails, orphan particle retention, 3D width rules and ribbons. The
final oracle has 3544 records across the previous main-stream cases and 29 trail scenarios.
All 3544 match at the unchanged 2e-4 tolerance. Two successive Editor runs agree on all 3544
values after adding regular ribbon GPU captures.

Measured Unity 6000.0.71f1 particle and trail arithmetic (`tests/projects/particlestreams`):

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

## Integrated camera MSAA sample selection

Reviewed and cherry-picked the camera delegate's four commits as 1c962e0c, 7f1d7021,
81b54879 and 89a50e33. An explicit HDR target now supplies its own samples. Quality
samples belong to the screen and to the HDR intermediate used when converting to an LDR
target; explicit buffer bindings bypass that intermediate. HDR resolves before LDR byte
quantization, including alpha. Supported-sample API requests round down to a supported
power of two on the measured reference device. Only FrameBuilder.cs and SystemInfo.cs
changed at runtime; Rust, protocol/native ABI and cache formats are unchanged.

The original 115 clipping quality-MSAA image failures and 41710 differing channels are
fixed. Parent Linear, Gamma and LinearMSAA EACH pass 1211 observations, 266 images and
17432576 channels with the existing comparator. The historical failing images and
comparison remain preserved. The prior graphics single-sample LDR target counterexample
also passes, so this does not simply turn off quality AA for explicit targets.

The broader camera oracle records 948 observations and 195 complete float images per
color space. Parent verified both independent Native pairs and committed float references
byte-for-byte. Parent matches all observations, 174/195 Linear images and 183/195 Gamma
images at the original 2e-4 tolerance. It retains 19 valid Linear failures and ten Gamma
failures: image-effect precision/color conversion, six Linear screen readbacks and HDR
partial viewport placement/filtering. Two inactive-source resolve captures per space also
fail; Native asserts on those inputs and their untouched destinations were not initialized.
They remain in strict totals, without being counted as valid rendering successes.

Parent managed build/API check pass, zero errors in 2266 types; all 275 combined UI tests
pass without skips. Repeated graphics regressions retain 80/80 records, resources 539/539
values and 62/62 images, builtinrp 830 matching values plus 124 inherited expected values,
and builtinfx 213 matching values plus two inherited expected values. Existing image
comparators pass. HDR pre-quantization changes many edge bytes and generally reduces Native
error; the delegate's complete metrics preserve the two small mean-error increases too.
No expected sets or tolerances were widened. Colors remain 98402 total, lifecycle 8671,
transitions 253 samples/831 callbacks and UI Toolkit byte-identical to its independent
baseline across metadata, eleven interactions and three images.

Evidence, exact commands and limits: tests/projects/cameramsaa/README.md and its Evidence
directory, with parent captures/logs/comparisons in ~/.cache/compat/cameramsaa-parent-*.
Authored settings remain Linear; Gamma copies live under Library. The old MSAA gap row is
closed for these measured paths, and image effects, screen readback and HDR partial viewport
are now separate open rows. This is not full camera or engine parity.

## Integrated camera effects, viewport and explicit readback

Reviewed and cherry-picked the follow-up as ea141243, 7e48b454, 36c417d1,
d192d13a and dbdbf81e. Offscreen effect sources retain target precision/encoding
and samples. Native's default-format first destination and direct final destination
in chains are measured. HDR intermediates now exist at one sample too, use camera
pixel dimensions for partial rects, and seed existing target color for Depth/Nothing.
Explicit attachment ReadPixels copies numeric values without an invented destination
gamma conversion; the format/read-write constructor ignores sRGB requests in Gamma.
Only FrameBuilder, RenderTexture and Texture2D changed at runtime. No Rust, protocol,
native ABI or shader-cache change was required.

Parent validation repeats the complete and focused fixtures in both color spaces.
Native pairs and committed references are byte-identical across all 3261 observations
and 433 images per full matrix, and all 481 observations/54 images per focused matrix.
Parent reproduces 948/948 observations and 187/195 Linear, 193/195 Gamma images in
the original matrices. Expanded totals are 3201/3261, 381/433 Linear and 3219/3261,
405/433 Gamma. Focused chains/clear preservation pass 481/481 observations, 54/54
images and 3268608/3268608 channels per space at the original tolerance.

All remaining full-matrix discrepancies are retained and classified: four rejected
inactive-source resolves per space, 24 public null-framebuffer captures per space,
and 24 Linear GameView/engine-screen comparisons. Native in-frame attachment records
prove distinct Editor system-framebuffer and GameView contents, including opaque versus
fractional alpha. Inactive resolve asserts and then copies the current framebuffer,
not the initialized destination sentinel or the requested unbound source. These cases
do not justify fabricating Editor window contents or a global gamma adjustment. Screen
source equivalence and invalid-input effects remain uncertified, not passing cases.

Managed build/API-check pass, zero errors in 2266 types. All 275 unit tests pass.
All three clipping matrices remain fully passing. Graphics/resources/builtinrp/builtinfx
checks retain their documented expectations, colors 98402, lifecycle 8671 and transitions
253 samples/831 callbacks. UI Toolkit metadata, eleven interactions and three PNGs are
byte-identical to the independent baseline. Three builtinfx images now match every Native
pixel: alpha_to_mask_no_msaa, hdr_half_target and hdr_on_no_msaa.

Parent checker commit 4b25b4f6 removes hdr_half_target from builtinfx's old exclusions,
so 92 images are checked normally with 11 inherited expected images. That checker also
used to report errors while exiting successfully. It now fails for unexpected values,
missing/wrong-size images, pixel mismatches and an empty reference. Seven focused tests
pass. Existing tolerances and the other expected cases are unchanged.

Evidence and commands: tests/projects/cameramsaa/README.md and Evidence/Followup, plus
~/.cache/compat/camerafollowup-parent-*. Authored settings remain Linear. The initial HDR
layer experiment also exposed Color-property transfer above one; its Vector-based camera
fixture isolates camera behavior and the color issue is tracked separately for an oracle.
Next work separates material/color transfer from DynamicsManager native scene settings,
with disjoint files and private builds. Neither full camera nor full engine parity is claimed.

## Integrated HDR and Gamma material color transfer

Reviewed and cherry-picked sixteen ordered delegate commits, c34b4824 through
8c67b55b. The runtime changes are 820ac0d5, 3190d697 and f5cd1e0b. Materials now
apply the selected shader's Color/HDR/Gamma binding rules, including built-in
sources. RGB above one uses the measured power-2.2 transfer. Converted getters
reproduce Native round trips without replacing assigned source data; negative
converted RGB clamps on getter inverse transfer while assigned GPU values remain
negative. Defaults clamp before initial conversion. HDR alpha remains unchanged.

MaterialPropertyBlock SetColor converts at assignment independently of declaration,
while SetVector and float overrides stay raw. Typed block storage and synthetic
renderer material copies prevent double conversion. Rust receives working-space
material/override values and converts only shader defaults. Shader flags come from
the actual selected ShaderLab source, using names without allocating property IDs.
General Mathf/Color, camera/readback code, packet layouts and native entry points
are unchanged. Metadata is parsed in memory; no persisted layout changed, so shader
cache 13 remains valid. Matching rebuilt CLI and managed artifacts are required.

Parent validation:

- Both Native pairs and committed references are byte-identical per color space.
  Parent Linear/Gamma EACH pass 2986/2986 observations, 424/424 complete 4x4 buffers
  and 27136/27136 channels at the frozen 2e-5 tolerance. Totals: 5972 observations,
  848 buffers and 54272 channels. No missing/extra cases or exclusions.
- Initial independent Linear baseline matched 2364 observations and 190 buffers;
  Gamma already matched every case. The delegate retains byte-conservation checks
  for Gamma and Linear raw Vector/ordinary Float controls.
- CLI and managed builds pass. API-check: zero errors in 2266 types. Managed tests
  306/306, Rust app tests 18/18 and checker tests 13/13 pass without skips.
- Repeated graphics 80/80, resources 539/539 and 62 images, builtinrp's existing
  830 matching/124 expected values and 33 normally checked images, and builtinfx's
  213 matching/two expected values and 92 normally checked images pass. Existing
  image expectations and tolerances are unchanged; hdr_half_target remains checked.
- All three clipping matrices remain 1211 observations/266 images/17432576 channels
  each, colors 98402 total, focused camera 481 observations/54 images per space.
  UI Toolkit metadata, eleven interactions and three PNGs are byte-identical to
  the independent baseline. Its inherited Native label-width discrepancy remains.

Only the builtinfx variant image and three numeric readbacks changed in the
delegate's complete before/after sweep. All three getters now equal Native exactly;
variant mean max-channel error fell from 1.1788333 to 0.003 and worst error 27 to 8.
One private Vulkan startup failure produced no captures; its log and a successful
fresh-directory retry are preserved. Parent's complete validation needed no retry.

Commands, counts and limits: tests/projects/materialcolortransfer/README.md and
Evidence, with parent captures/comparisons in ~/.cache/compat/materialcolor-parent/
and build/API/test logs in ~/.cache/compat/materialcolor-parent-*. Shader-switch
reinterpretation, arrays, undeclared color properties, nonfinite GPU behavior and
other releases remain unmeasured. PhysX native scene settings remain in a separate
active worktree; no WIP physics commit is integrated here. Full engine parity is open.

### Integrated DynamicsManager settings and kinematic lifecycle, 2026-10-31

Staged the completed PhysX candidate `0931158f` on a separate integration branch
from the HDR parent `52b88c11`. The delegate's eight introduced island-clone
failures prevented immediate integration. Added independent Native probes for
long sleeping trigger overlap, callback ordering/removal, immediate type setters,
unsorted contact points and component-registration barriers.

Trigger correction `e407dcdf` sends Stay after Enter in the same simulation and
through sleeping overlap. Kinematic correction `841a2135` preserves lazy native
allocation, applies both type transitions to existing bodies, and starts real
zero-displacement activity for activation/cloning. It removes all newly failing
clone records without changing the PhysX SDK algorithm or comparison tolerances.
Rejected allocation experiments and their broader regressions are retained.

The rebuilt integrated CLI, managed engine and shim7 match 5027/5055 observations:
4775 exact and 252 passing motion-rounding records. Both Native sets agree on all
5055 values; the original 13 profiles and four contact profiles pass completely.
No failing observation is introduced relative to the independently built shim6
baseline. The delegate's 273 failing records drop to 28, all retained kd
fresh/activation island-history cases. All clone cases now pass.

Extended triggers match all 1152 callback/order observations and 1575/1728 overall;
153 sleep-state differences remain. Immediate setter observations pass 15/15.
Physics tests pass 41/41, integrated physics/UIElements/Yoga/Canvas tests 302/302,
and API-check reports zero errors in 2266 hand-written types. All six existing
physics regression outputs are byte-identical to the delegate baseline, including
the same eight broad-physics failures and documented joints differences.

Main ABI remains 5, PhysX shim is 7, shader cache remains 13. Parent build and API
logs, complete raw evidence and rejected experiments are documented in
tests/projects/physicsscenesettings/ParentEvidence/README.md. The original delegate
evidence and Claude backup remain preserved. Public Physics.ContactEvent, older
releases and the listed island/sleep cases remain open; this is not full engine
parity.

### SDK shape lifecycle closes the remaining DynamicsManager matrix, 2026-10-31

Merged the continuation delegate's Native ID/flag traces and rejected candidates
without changing runtime. Its delegated scope excluded SDK edits. The parent
then measured the Unity fork's differing SDK path directly and corrected shape
flag handling on a separate branch, with production runtime in `9491a8bc`.

Unity preserves a broadphase element ID while changing simulation/trigger flags.
The SDK now updates that volume's metadata and marks filtering dirty. Trigger
pairs include trigger/trigger overlaps, and fresh trigger creation executes the
pair filter callback. Live box updates preserve shape identity. Activation fully
realizes physics before MonoBehaviour Awake, including a clone destroyed before
its first simulation. Contact and trigger event phases remain separate, with
trigger messages first. IgnoreCollision survives Collider/GameObject deactivation.

The complete rebuilt parent gate passes 5055/5055 observations: 4803 exact and
252 within the unchanged motion tolerance. All 28 retained island-history cases
now pass without introducing any failing observation. The additional two-run
Native controls contain 1152, 1440 and 1848 observations. Every callback, ordering,
raw-contact, ignore-state and motion record passes; each control retains its same
three pre-simulation sleep records. Original long triggers remain 1575/1728 with
153 known sleep-state differences; immediate setters pass 15/15.

Integrated validation passes 318 managed tests, zero API errors in 2266 types,
and 80/80 graphics observations. Defaults, controller, rayhits and joints keep
byte-identical outputs. Forces pass 608/608 with passing rounding changes. Broad
physics improves to 22/29 scenarios: all 85 old stack failures resolve and every
3D scenario passes. The seven 2D scenario differences and 133 failing values are
retained without new failing values.

The new native binding is PhysX shim8; main ABI5 and shader cache13 are unchanged.
SDK archives use source/header contents, compiler inputs and a cache format
version for their BLAKE3 key. A complete SDK/CLI rebuild and a warm cache rebuild
pass. Full evidence, artifact hashes, explicit remaining sleep failures, prior
rejected experiments and commands are in
tests/projects/physicsscenesettings/SDKLifecycleEvidence/README.md. Public
ContactEvent, older releases and unmeasured collider lifecycle cases remain open.

### Active-body sleep and target timing, 2026-10-31

Integrated delegate commits `3ed4006e`, `6bfb5563`, `88c8919e` and `fc341e88`
on a separate integration branch. Move commands now reach PhysX immediately;
kinematic pose and target commands merge their measured components until the
next simulation. Transform sync replaces the entire target. Dynamic Move
commands teleport immediately, while a subsequent Sleep keeps the Transform
unpublished. IsSleeping reads PhysX state without a synthetic sleep counter.
No native, SDK, main ABI5, shim8 or shader cache13 change was needed.

Two independent Unity recordings agree byte-for-byte on 20700 observations.
The parent rebuilt managed output and reproduced all 20700 exactly, along with
1728/1728 extended trigger observations and the three SDK controls at 1152/1152,
1440/1440 and 1848/1848. This resolves all 162 previously targeted sleep
differences. The complete archived matrix still passes 5055/5055, 4803 exact
and 252 within the original motion tolerance; the parent independently checked
all recorded values against both original Native recordings.

The parent's integrated tests pass 327/327, including nine new command-sequence
cases. API-check reports zero errors in 2266 hand-written types. The delegate's
six broad regression outputs remain byte-identical to SDK8 and graphics pass
80/80. The parent corrected the evidence counter to count the 80 graphics cases
instead of the two JSON wrapper fields; capture values and tolerances did not
change.

A separate inactive-body probe has two byte-identical Native recordings of
1050 observations. Only 318 pass, with 732 retained differences in commands
issued while outside the scene and subsequent reactivation. The parent also
reproduced that output byte-for-byte, confirming no new differences. These
newly measured gaps, seven existing 2D scenarios with 133 failing values, public
ContactEvent and older releases remain open. Evidence and commands are in
tests/projects/physicsscenesettings/SleepEvidence/README.md.

### 2D sensor integration and callback phases, 2026-10-31

Integrated the separate 2D continuation `169542f6`, `e98e598f`, `2102b330` and
`ab1f9b0b` with the accepted active-body sleep fix. Sensors retain the initial
contact update and also evaluate actual integrated poses after the solver and
TOI. Solid contact manifolds and impulses retain their original phases. Trigger
callbacks precede solid-contact callbacks, preserving order within each kind.
The broad falling-circle trigger now enters at step 44 and exits at step 56,
matching Unity. No synthetic callback, one-step offset or tolerance change is used.

Independent Native pairs agree byte-for-byte on 18491 expanded timing records
and 5720 mixed-order records. The parent rebuilt the combined managed engine and
reproduced the delegate's complete timing, mixed-order and broad outputs without
any changed value. Timing passes 18275/18491 and mixed order 5560/5720. Together
they resolve 1343 failing keys without introducing any. Broad physics improves
from 22/29 to 23/29 scenarios, passing 11495/11626 values with 131 retained
failures in six 2D scenarios. All 13 broad 3D scenarios remain unchanged.

The combined parent's integrated tests pass 332/332, including the nine sleep
cases and five sensor/order cases. API-check reports zero errors in 2266 types;
GPU graphics pass 80/80. Fresh Joint2D, Effector2D and forces outputs are
byte-identical to the delegate and accepted baseline. Their previously reported
rounding and measured contact-order alternatives remain explicit.

The parent also reran all 21 scene-settings profiles, passing 5055/5055 with
4803 exact and 252 passing rounding. Every raw file remains byte-identical to
SDK8 acceptance. Active-body sleep passes 20700/20700 exactly, extended triggers
1728/1728 and SDK controls 1152/1152, 1440/1440 and 1848/1848. Inactive-body output
remains unchanged at 318/1050 with its 732 differences.

The 376 retained timing/order observations cover teardown, lifetime and sleep
with static transform changes. They remain part of the comparisons. Older
releases and full 2D/engine parity remain open. Commands, rejected candidates,
complete failures, parent checks and artifact hashes are in
tests/projects/trigger2dtiming/README.md.

### Inactive body poses and final clone placement, 2026-10-31

Integrated inactive-body delegate commits `b7dac550`, `ac764eae`, `a8a4927f`
and `26ae4baf` with the accepted 2D sensor/runtime changes. Inactive bodies retain
a physics pose separately from Transform. Inactive Move commands are ignored;
pose setters change the retained physics pose and activation takes the current
Transform pose. Dynamic velocity survives deactivation, kinematic velocity writes
are ignored, and cloned motion starts at zero. All 732 original inactive failures
resolve, passing 1050/1050 exactly. Another pair of agreeing Native runs contains
4668 sequence records; the final parent reproduces all 4668 exactly.

Parent review added independent Native controls for clone construction: 160
body/placement records and 12 Transform-only records, with two byte-identical
recordings each. They exposed 10 newly failing keys in the inactive-only candidate,
along with previously unmeasured placement failures. The parent rejected that
state before merging it into the continuation branch. Bodies now snapshot their
initial pose after clone placement. Unparented GameObject clones preserve copied
local state unless explicit coordinates are supplied; null-parent flags have
the same measured result. Non-null parent placement keeps its requested mode.

The first placement correction overwrote copied RectTransform anchors and failed
27 Canvas observations. The final correction preserves those copied fields;
all 8671 Canvas observations pass again. Both rejected candidates, every failing
key and the Native inputs remain in ClonePlacementEvidence. No reference,
tolerance or acceptance mask was changed. Parent runtime corrections are
`ddefcbee` and `8935a346`.

The final combined parent passes 353/353 tests and API-check with zero errors in
2266 hand-written types. Every fresh matrix file is byte-identical to accepted
SDK8: 5055/5055 observations, 4803 exact and 252 passing original rounding.
Active-body sleep remains 20700/20700 exact; extended triggers, SDK controls and
all immediate/order/component/barrier controls pass. Graphics passes 80/80,
messages 316/316, and the lifecycle output is byte-identical to Native.

Broad physics remains 23/29 scenarios with the same 131 failing 2D values.
Expanded timing/order retain the same 216+160 failures. Joint2D, Effector2D,
defaults, controller, joints, forces and rayhits pass their existing gates.
The main ABI5, shim8 and shader cache13 are unchanged. Full parent evidence and
reproduction are in physicsscenesettings/InactiveEvidence/ParentIntegration;
serialized inactive objects edited before their first physics read, additional
hierarchy/scale cases and older releases remain unmeasured. This is not full
engine parity.

### Serialized inactive body initialization, 2026-10-31

Integrated `af5d3398`, `3ded1eac`, `f807cd00` and `7172284c` from the isolated
loading workstream. ObjectGraphBuilder now captures Rigidbody poses after all
serialized fields, parent links and flattened prefab overrides are complete,
before creation callbacks and scene activation. Getter-triggered capture is
removed. Runtime attachment and post-placement clone snapshots remain intact.
Kinematic deactivation clears the velocity computed from its last target;
dynamic bodies retain their actual motion.

The new fixture has two independent Native recordings for each document order.
All four recordings are byte-identical at 4656 observations each. They cover
scene bodies, nested prefab instances and overrides, Resources assets and
instances, active/inactive and dynamic/kinematic objects, parented poses and
edits before the first physics read. Both parent engine replays pass exactly,
9312/9312, resolving 98 Transform-first and 82 Rigidbody-first observations.
The intermediate candidate's 42 failures per order remain archived.

Fresh integrated parent validation passes 371/371 tests, API-check with zero
errors in 2266 types, all 5055 matrix observations and every existing focused
control. All 21 raw matrix files remain byte-identical to SDK8 acceptance;
4803 observations are exact and 252 use the unchanged motion tolerance.
Graphics80, Canvas8671 and messages316 pass; lifecycle is Native-byte-identical.
The same 131 broad 2D differences and 376 timing/order differences remain.
Existing defaults, joints, controller, forces, rayhits, Joint2D and Effector2D
gates pass. Main ABI5, shim8 and shader cache13 are unchanged.

Source, four Native captures, every baseline/candidate difference and exact
commands are in tests/projects/inactive3dloading. Independent parent evidence
and artifact hashes are under Evidence/ParentIntegration. Non-unit scale, other
rotations, further loading mechanisms and older releases remain unmeasured;
this does not claim full engine parity.

### 2D contact removal, sleep and receiver ordering, 2026-10-31

Integrated the isolated lifecycle workstream commits `cf05f542`, `848d6682`,
`33156705`, `97158ae0` and `91d4adf1`. Real Box2D removal notifications now drain
after fixture/body mutations finish, following both receivers of an in-progress
notification. Collider/GameObject removal and simulated=false retain their
separately measured callback timing. Disabled simulation preserves the body and
sleep state. Static movement and newly discovered static contacts wake neighbours
at the Native moment. Collider creation order selects receiver order, including
late Rigidbody2D attachment. DestroyImmediate rejects 2D physics objects and
hierarchies during 2D callbacks; removal of only a Rigidbody2D converts surviving
colliders to static without synthetic exits.

Two independent Native recordings agree on 25356 lifecycle and 1152 receiver
observations. The fresh parent replay matches every value exactly. All 376 old
timing/order failures resolve: timing18491/18491 and mixed order5720/5720 pass,
retaining their original 46 and four passing numeric-rounding records. Every
callback, ordering, step, sleep and touching observation is exact. Ten broad
kinematic values also resolve. Broad physics passes 11505/11626 values and
23/29 scenarios, retaining 121 failures in six 2D scenarios with no introduced
failing key or value.

The combined parent includes the serialized-body loading fix and passes
384/384 tests and API-check with zero errors in 2266 types. Fresh loading9312,
inactive1050, sequence4668, active20700, clone172 and all trigger/SDK controls
pass. Matrix5055 passes with 4803 exact and 252 original rounding observations;
all 21 raw files remain byte-identical to SDK8 acceptance. Graphics80,
Canvas8671, messages316, Native-identical lifecycle and existing physics
comparators pass. Main ABI5, shim8 and shader cache13 remain unchanged.

The stage README, independent Native pairs, complete rejected candidates and
retained failures are in tests/projects/trigger2dlifecycle. Its parent archive
records the fresh combined captures and hashes. Further multi-pair reentrancy,
hierarchy/scale cases and older releases remain unmeasured. Full engine parity
remains open.

### Combined query geometry and captured box skin, 2026-10-31

The rejected query candidate and the separately measured BoxCollider2D skin fix
are now combined. Box skin is captured on the measured geometry/registration
transitions. Query contact selection recognizes an unrounded target box even
when its captured skin exceeds the current global query skin. The first merge
introduced 23 selection values; that output remains archived. The final
correction introduces none against either the actual frozen accepted runtime
or the starting query candidate in any of nine query-control suites.

Original broad queries pass all7375 assigned records, resolving76 failures.
Broad physics matches11581/11626, retaining45 motion values across bounce,
stack and kinematic scenarios. Main query controls match52059/55251; their
3192 remaining point/normal failures repeat initial-contact geometries across
directions. Fine selection retains6 failures, selection1 and edge-shape
grouping20 union-key failures. Boundary64, isolated48, box skin644 and initial
threshold6256 pass. No tolerance, expected value or key was removed.

New independent Native frame/center recordings agree on75168 observations and
2592 cases. The combined runtime matches72439, with2729 retained and zero
introduced failures. The full expanded-motion replay keeps3362360 observations
in69 scenarios exactly unchanged; only the three original query scenarios
change. Broader motion failures and all rejected experiments remain explicit.

Fresh combined tests pass494/494. API-check reports zero errors in2266 types.
Matrix5055 retains4803 exact and252 original rounding observations, with all21
raw files byte-identical to SDK8. Loading9312, lifecycle25356/receiver1152,
timing18491, order5720 and all focused3D gates pass. Existing physical, graphics80,
Canvas8671, messages316 and Native-identical lifecycle checks remain accepted.
All eight older frozen snapshots verify unchanged. ABI5, shim8 and shadercache13
are unchanged. Commands, all deltas and artifact hashes are in
tests/projects/query2dskinmerge; new Native controls are in query2dties.

This closes the original broad query failures while retaining the measured
expanded-query and motion gaps. Non-Box skin transitions, GetShapes with changed
global offsets and older releases remain unmeasured. Public3D ContactEvent/shim9
is still an isolated unaccepted branch. Full engine parity remains open.

### Portable Unity references and external run artifacts, 2026-10-31

Probe runs no longer publish evidence, candidates, comparisons, gates or build
logs in Git. Those files were archived under the XDG unbound validation cache
before removal. Historical commits are preserved; no history rewrite or force
push was used. Earlier entries describe their original run locations, which are
now obsolete repository paths.

Canonical Unity data lives in Reference/unity-<version> files or versioned
profile directories. Repeated Native captures and Before/Final/Followup copies
were reduced to one current oracle per release without removing actual test
cases. Portable run, compare, report and verify scripts read these references
and keep every generated output outside Git. New-release recording selects
the matching Editor and validates independent repeats before publishing.

Path migration retained all previous comparisons: passing suites still pass;
known camera, realtime-GI, initial-query and motion differences remain visible.
Default reference-only verification explicitly says that no engine capture was
compared. Full before/after gates consume explicit actual and baseline work
directories and report all retained and introduced values.

### Shared physics integration and public contacts, 2026-10-31

The query coincidence fix, per-point entering velocity and public 3D contact
implementation now run together. Query and simulation code share the Box2D
collision and sweep algorithms; the three QueryParity production copies are
removed. Operation-specific precision stays explicit in the shared algorithms.
No unmeasured release split was introduced.

Main queries match52347/55251 and frame/center ties72679/75168, resolving528 prior
values with no new failures across the nine suites. The new contact-feature
oracle matches174044/181440. Entering ContactPoint2D velocities are captured at
each point before solving, while Collision2D retains point0 for its aggregate
velocity. This resolves12 dynamics-control values; the complete dynamics suite
matches22341/24082 and retains1741 failures.

Public ContactEvent now uses actual SDK headers, pairs, points and impulses,
with the measured40/40/48-byte layouts and borrowed-array lifetime checks. All
29628 profile observations pass the original physical bounds. Capsule creation
and query-frame fixes resolve all832 failures of the rejected public feature;
5610 expanded sweep values match exactly. Two raw process-hash rows differ in
independent Native runs too and remain visible in comparisons. Local scenes,
asynchronous lifetimes, non-unit capsule transforms and older releases remain
unmeasured.

The combined managed build passes530/530 tests without skips. API checking finds
zero errors in2274 handwritten types; PhysicsModule regeneration matches. The
5055-record scene matrix retains4803 exact values and252 original rounding
values, with all21 captures byte-identical to acceptance. Loading9312,
lifecycle25356/receiver1152, timing18491, order5720, focused3D, physical regression
comparators, graphics80, Canvas8671, messages316 and general lifecycle all pass
their existing gates. Full motion and rotation comparisons introduce no failing
keys. Broad physics retains45 failures; expanded motion retains1423981/3399409.
These retained gaps remain open.

The public contact exports require shim9. Main ABI5 and shadercache13 are
unchanged; the SDK is unchanged from the accepted native8 baseline. Matching
private CLI, managed and native artifacts were checked together. Disposable
captures, reports, rejected invocations, hashes and the frozen integration
snapshot are in the external Unbound validation cache. Only sources and one
canonical Unity reference set per release enter Git. No history was rewritten.

### Particle force-field rotation, 2026-10-31

Force-field rotation now relaxes stored velocity toward the transformed tangent,
using distance-sampled speed/attraction curves and sequential field application.
Direction precedes rotation and drag follows it. Two independent Native recordings
agree across all 2346 controls, which now pass without changed tolerances.
Randomized curves and rotation randomness remain unsupported and warn.

The combined branch passes 551 managed tests without skips. Separate particle
unit tests remain 26/30 with the same four inherited failures. API checking finds
zero errors in 2274 types; PhysicsModule regeneration matches. Broad particles
match 1448/1560 records, resolving 301 scalar differences and retaining 598 with
zero introduced failures. The original rotation scenario passes 16/16. Noise
23646 and streams3544 pass, with all three stream image buffers exact. Four broad
physical regression outputs are byte-identical to the accepted parent.

Solver-body and expanded UI callback experiments delivered probes and tests only;
their runtime candidates were rejected. Solver controls retain 2219 failures,
and UI callbacks retain 5053; both introduce zero failures in the delivered code.
The old transition oracle was renamed without changing its bytes. Its 253 samples
and 831 callbacks still pass. Fresh UI regression copies preserve all 98402 canvas
colors and 8671 lifecycle observations; UIToolkit records and three images match
the independent baseline exactly. Disposable projects and runner outputs now use
explicit private artifacts and the Unbound cache.

Main ABI5, shim9 and shadercache13 are unchanged. All run data, rejected patches,
logs, reports and artifact manifests remain outside Git. Older releases and
remaining physical, particle and UI gaps stay open; this is not full engine parity.
### Model import materials, LOD groups and avatars, 2026-10-31

The model import probe (tests/projects/modelimport) records 19 FBX models from Blender and
hand-written ASCII files under every material import mode, location, naming and search
setting, with texture search, LOD names and avatar setups. Two independent Editor runs per
color space agreed byte for byte before the Gamma and Linear references and the 35 material
descriptions were published.

FBX materials are described as Unity's importer describes them and built on the managed side:
"Import via MaterialDescription" runs the built-in FBXMaterialDescriptionPreprocessor and every
loaded postprocessor in order; the legacy conversion, its "No Name" material and the external
material search are reproduced. LOD-named renderer nodes make the root's LODGroup. An Animator
and the `<model>Avatar` sub-asset exist only with an avatar setup; Animators resolve generic
avatars. Built-in shaders now take their properties from the release's sources.

Unbound matches 6032/6033 values in both color spaces and all 35 descriptions. The remaining
value is an Autodesk Interactive cutout material's `_DstBlend`, which the Editor reports as 0
although its preprocessor sets 10 (recorded in the probe README). URP's material preprocessors
run but are not yet compared with the Editor; material animation curves are not imported.

Regression, against the 58eb44fd integration build: graphics80, resources539/62 images,
builtinrp, builtinfx (only the raw material CRC values, already an expected difference, change),
rect clipping three profiles, canvas colors two profiles, focused camera two profiles, materials
1004, blendshapes and lod keep their results; uitk keeps its earlier image difference. Managed
tests give the same 746/848 as the base in this environment (the failures need the native
PhysX and text libraries on the test path). api-check: 0 errors in 2278 hand-written types.

### Lifetime boundaries and combined model import, 2026-10-31

SetParticles now removes nonpositive input lifetimes before age encoding while
retaining tiny positive inputs that encode to zero. Aligned four-slot compaction,
custom-stream movement, offset truncation and overlong-lifetime clamping follow
two agreeing Native recordings with 30080 records. Global aging and emission
subdivision are unchanged. The control resolves160174 scalar observations and
retains90 existing isEmitting differences, with zero introduced failures.
The stopped Emit expectation now follows the separately recorded Native state;
particle unit tests improve to27/30 with three inherited failures remaining.

The exact model-import handoff7696b9c6 was merged after the lifetime correction;
uncompleted URP work was excluded. Its Rust sources match the copied private CLI,
and fresh managed assemblies include both features. The combined selection passes
697 tests without skips. API checking reports zero errors in2278 types, with both
PhysicsModule and Editor CoreModule generation matching the checked-in files.
Model import retains6032/6033 per color space and35/35 descriptions. The single
Autodesk Interactive cutout blend value remains explicit.

Fresh particle, force-field, noise and stream comparisons preserve their accepted
results: broad1448/1560 with597 retained scalar differences, force-fields2346,
noise23646 and streams3544/three exact images. Physical broad, Joint2D, effector
and force raw outputs are unchanged. Graphics, resources, builtinrp, clipping
three profiles, colors two profiles, focused cameras and UIToolkit reproduce
the recorded model-import baseline. Builtinfx changes only its already-expected
raw material CRC values. The Native UIToolkit label-width/image difference is
unchanged; before/after captures match exactly.

All artifacts and partial-failure reports remain in the external validation cache.
The model-import change requires a CLI with builtin-shader-source; the earlier
frozen CLI must not be paired with these managed assemblies. Full engine parity
and the remaining physics, particles, animation/audio and UI gaps remain open.

### URP model material validation, 2026-10-31

The probe also records a URP project (the urp probe's pipeline asset and packages), and every
profile is now recorded with GPU Editors, with values identical to the earlier captures. In
URP the render pipeline's own preprocessors (package sources) run after the built-in one;
a material switching shader keeps the old shader's defaults for names both declare; slots
without a material take the pipeline's default material; and imported materials are
validated by their shader's ShaderGUI when Unbound has that class (URP's, projects'). URP
matches 7011 of 7687 Native values. The complete union has 7768 keys and 757 failures:
550 missing, 126 differing and 81 extra keys. Legacy-mode URP materials, Shader Graph
material queues and GI flags, and `GetTag("RenderType", false)` on validated Lit
materials still differ. Root integration reproduces the delivered URP values with
zero changes or newly failing keys; all35 material descriptions also match. Built-in
profiles keep 6032/6033. The material-colour regression set, materials, blendshapes, lod,
urp and urp2d keep their results (one URP skybox capture differs by at most 1/255 in 12310
channels); managed tests keep the same text and particle failures as the base.


### Panel transition callback phases, 2026-10-31

Integrated the measured panel-wide animation phases from f44954e6. Each value type
samples its owners before event dispatch; callbacks can retarget a later type in
the same update, while same-type callback events wait for the next batch. Picking
and layout validation no longer advance animation time. Inline destination writes
and detachment preserve the measured values and pending callback behavior.

The complete callback reference matches byte-for-byte: 718 samples, 3428 callbacks,
and all78854 observations. This resolves all5053 previous failing keys, with zero
retained or introduced failures. The added inline controls match110 samples and
132 callbacks exactly against two agreeing Editor recordings. The original253
samples/831 callbacks also pass. Source, runners and one release reference are in
uitransitioncallbacks and uitransitioninline; run artifacts stay in the cache.

The delegate passed557 managed tests, the rendering gates and33552 focused physics
observations. Root combined verification with URP material validation and the
particle lifetime changes passes703/703 managed tests, including all697 previous
tests and six added callback controls. The separate particle suite retains its
three known failures, with27/30 passing. API verification reports zero errors in
2279 types; regenerated Physics and Editor modules match the committed files.
The combined behavior and rendering replay is complete, with zero newly failing
observations. All2029 graphics output files match the previous accepted model and
lifetime build byte-for-byte, including UIToolkit JSON and three images. Each
clipping profile passes1211 observations and266 images; canvas colors pass98402
observations; focused cameras pass481 observations and54 images per color space.
The strict UIToolkit comparator still fails against Native in both before and
after captures, retaining the same Label width and image discrepancy.

Noise23646, streams3544 and force-field2346 controls pass. All20 broad-particle and
stream PNGs, plus broad physics, Joint2D, effector and force raw outputs, are
byte-identical to the previous accepted build. Lifetime retains90 emission-state
differences, broad particles retain597 scalar differences, and URP retains the
757 complete-union differences documented above. None are excluded or reclassified
as passing. Other transition property combinations and full engine parity remain
open.

The immutable artifact set is panel-models-native9, outside Git in the validation
cache. Main ABI5, PhysX shim9 and shader cache13 are unchanged. A checker expected
count omitted81 extra URP keys; the corrected complete-union report preserves them.
A Vulkan Device(Lost) startup failure produced no captures; its log remains in the
cache, and one fresh-directory retry passed. Completed captures were reused.


### Particle emission and text measurement follow-up, 2026-10-31

Integrated feec4436 and 23e0fe1e as independent changes. Emission observations now
respect the module switch without treating zero rates or start delays as disabled
emission. Playback snapshots restore simulation data while preserving the current
Play/Pause/Stop state. Empty Stop/Clear, zero Emit and stopped simulation follow the
measured state and clock behavior. The complete lifetime oracle passes all 30,080
records in the delivered branch, resolving the previous 90 flag differences. The
new 13,604-record emission control retains 72 count differences; all state flags
and times match, with no introduced or changed retained values.

Text measurement invalidates its cache when stylesheet matches or inherited style
inputs change, including when declarations override the final values to equality.
This resolves the inherited Label width of 54 instead of 53. Delivered focused
controls pass all 608,384 observations; the existing UIToolkit JSON union passes
36,846 observations. All three PNGs remain unchanged, retaining their small Native
pixel differences. Generic font metrics and pixel rounding were not adjusted.

Root combined managed verification passes 837/837 tests, including all 703 previous
tests, 122 emission cases, four measurement-cache cases and eight text/font cases.
The separate particle suite retains the same three failures. API verification
reports zero errors in 2,279 types; Physics and Editor generated files match.
Targeted combined capture replay is complete. Root confirms lifetime 30,080/30,080
with all 90 previous flag failures resolved; emission retains exactly the same 72
count values, with all flags and times matching. Text metrics pass 608,384/608,384,
and UIToolkit JSON passes 36,846/36,846, resolving the two original width values.
All three transition oracles, Canvas 8,671, colors 98,402 and each clipping profile's
1,211 observations/266 images pass. Broad particle failures remain 597 with identical
retained values. Physical outputs and all 20 particle/stream images are unchanged.
The small Native UIToolkit pixel differences remain unchanged, with zero introduced
or worsened channels. The immutable combined artifact set is emission-text-native9;
main ABI5, PhysX shim9 and shader cache13 are unchanged. Earlier model, camera and
material results remain in panel-models-native9. The text probe
can select another Editor release and writes that release into its disposable
project; only Unity 6000.0.71f1 Linux has been measured so far.
### Texture import mip filters and normal maps, 2026-10-31

The texture import probe (tests/projects/textureimport) records every mip level of 75 imported
RGBA32 textures with `GetPixels32`: box and Kaiser mipmaps in sRGB and linear, fade out, border
mipmaps, preserve coverage, alpha is transparency, alpha from grayscale and alpha source None,
NPOT sizes unscaled and scaled, the max size with both resize algorithms, and normal maps plain
and made from grayscale (standard and Sobel, eleven height scales, three sizes). Two GPU Editor
runs per profile (Gamma and Linear on Standalone, Android) agreed byte for byte before the
references were published; the Gamma and Linear references are identical.

The importer now builds mips from a float chain rounded per level (box as centred bilinear
sampling, Kaiser width 2 and alpha 4 while both sides are even), fades encoded colour towards
gray or the flat normal, filters border texels along the edge only and scales alpha for coverage.
Normal maps are stored DXT5nm (1, y, y, x) on targets without `UNITY_NO_DXT5nm` and XYZ
otherwise; grayscale normals filter the colour per level and scale the slope by
`0.08 * (10^heightScale - 1) * (width + height) / 2`. Resizing uses Mitchell to shrink, a cubic
B-spline to enlarge and truncating bilinear for the Bilinear algorithm, all on stored bytes.

Unbound matches 419/484 mip levels byte for byte in every profile (before: 49 Standalone, 54
Android) and 456 within 1. The remaining differences: the Editor's alpha-is-transparency fill
(27 levels of four cases), one preserve-coverage 2x2 level (up to 18) and 1-step rounding ties.

Regression against the untouched build: graphics, resources, builtinrp, builtinfx, rect clipping
three profiles, canvas colors two profiles, focused camera two profiles and uitk give byte-identical
raw outputs (uitk keeps its earlier image difference); baked and terrain lightmaps are identical.
Rust tests: unbound-assets 69 and unbound-runtime 38 pass.

### Additive and override animator layers, 2026-10-31

Additive layers subtract their clips' reference pose (the clip's first frame, another clip or
the clip itself at a time) and compose rotations as below·difference, the difference's vector
part scaled by the layer weight. Override layers complete what their states do not animate:
motion states without write defaults keep the value below for every channel of the controller,
write-defaults states restore only their own layer's channels. An empty state without write
defaults holds the layer's last values, including channels only the base layer animates; before
the layer has any, it passes the value below through, and contributes the default values while
transitioning. A rotation completed with the value below blends unnormalized.

The probe has 528 records in 48 cases; two independent GPU Editor captures were byte-identical
and are published as `tests/projects/animatoradditive/Reference/unity-6000.0.71f1.jsonl`.
Unbound matches 528/528 records (6336 values); the ddb1dff1 baseline matched 60/528. The animator
probe keeps 2236/2236 and the bundle animation probe 8/8. Animation unit tests pass; two
assertions now follow the measurement (a constant additive clip adds nothing; write defaults do
not restore the base layer's channels). API checking reports zero errors in 2278 types.
Humanoid layers remain out of scope.


### Texture and Animator integration build, 2026-10-31

Merged texture294630d8 and Animator1d1662f6 without discarding the SSD/job-runner
commits. The CLI and managed runtime were rebuilt together from1068b6a9; Cargo's
path-sensitive host build was refreshed, and the normal build did not fall back
to a runtime without compatibility stubs. Asset tests69/69, runtime tests38/38,
and the existing selected managed tests837/837 pass. API-check reports zero errors
in2279 types, with matching regenerated Physics and Editor modules.

The full managed selection contains1135 tests. Using the same current test assembly
against the frozen c59 parent Core gives1122 passes/13 failures; the candidate gives
1123 passes/12 failures. The Native-backed additive expectation correction in
AnimationTests is included in this differential. Its layer test now passes, and
there are zero newly failing test names. The nine Unbound.Tests.TextTests and three
ParticleSystemTests failures remain. This is not a claim that the full suite is green.

An immutable build input with all34 file hashes is kept outside Git. Root captures
confirm the original additive528/528, animator2236/2236 and bundle animation8/8.
All2029 rendering payload files match the previous accepted captures byte-for-byte.
One Vulkan Device(Lost) startup produced no captures; its failed log is preserved,
and one fresh-directory retry completed all rendering gates.

Strict texture comparison matches419/484 mip levels in each profile. At byte
granularity Gamma/Linear resolve576020 failures but retain33542 and introduce72;
Android resolves413658, retains33440 and introduces73. Every failed byte and its
expected/actual value remains in the cache report. This importer stage remains
unaccepted at tolerance zero; a separate continuation owns transparency fill,
coverage and rounding. No mip-level count substitutes for byte-level acceptance.

### Mixed write-defaults transitions, 2026-10-31

The additive layer probe now also covers transitions between a state with write defaults and one
without (both ways), in the base, an override and an additive layer, to motion and to empty states,
at layer weights 1 and 0.5, with script-set channels during a transition: 52 more cases, 1100
records. Two independent GPU Editor captures were byte-identical; their first 528 records equal
the previous reference byte for byte. Measured in 6000.0.71f1:

- In a transition, a motion state with write defaults has the default value for what the other
  state keeps without write defaults, including channels only lower layers animate (override
  layers: those channels blend towards their defaults, which neither state writes alone).
- A state without a motion and without write defaults that a transition leaves keeps the values it
  held when the transition began; as a destination it takes the layer's values of each update.
  This holds in base, override and additive layers and for either write defaults of the other state.
- In the base layer, a state without a motion and with write defaults adds nothing to a transition
  with a motion state: the motion state applies in full whatever the progress, also in the update
  the transition completes; alone it writes the defaults. Override layers blend it with the value
  below as before.
- States without write defaults keep what the Animator last wrote: values set by game code in
  between are overwritten.

Before the fix Unbound matched 1007/1100 records; after it 1100/1100 (13200 values). The animator
probe keeps 2236/2236 and the bundle animation probe 8/8. The managed tests have no new failures
(82 earlier text, physics and particle failures, all in the previous list) and no animation failures.

### Combined clone and Animator verification, 2026-10-31

Root merged the mixed write-defaults implementation and texture/text asset cloning
at840933bc. The combined runtime matches1100/1100 Animator records and13200/13200
values; the existing animator2236/2236 and bundle8/8 controls also pass. Root
verified both independent Native repeats against the canonical references,
including the unchanged first528 Animator records.

Texture2D clones own retained CPU mip snapshots and independent GPU allocations,
including unreadable textures. CopyTexture preserves retained CPU storage even
when readability is disabled. RenderTexture clones retain descriptors, start
uncreated, and Release clears an active target. TextAsset clones own their bytes
and use the measured valid/malformed BOM decoding behavior. Stored sRGB metadata
is distinct from the active project-color-space graphics format.

The main clone union matches14287/14340 values, resolves8006 and preserves all6281
previous passes. Its53 retained differences are45 sampler-bias GPU values, two
imported-source hide flags and six preexisting dirty-source auto-upload values.
Each separate color profile matches105/109 and retains four Alpha8 graphics-format
metadata differences. Root captures equal the delivered comparison rows exactly;
no passing value regresses and no residual is hidden by a tolerance change.

The same1151-test assembly against the previous and combined Core changes from
1123 passes/28 failures to1139 passes/12 failures. All16 clone/encoding cases now
pass; the same nine legacy TextTests and three ParticleSystemTests failures remain.
Selected tests pass853/853 with zero skips. API-check reports zero errors in2279
types; regenerated Core, Physics and Editor modules match the committed files.

All2029 rendering payload files are byte-identical to the preceding source-correct
texture/Animator build. Graphics80, resources539/62 images, clipping1211/266 images
per profile, colors98402, and focused cameras481/54 images per color space retain
their passing results. Existing built-in motion/effect and UIToolkit pixel gaps
remain visible. Build inputs and full test/value unions are cache-only. Native API12,
render protocol5, PhysX shim9 and shader cache13 are unchanged. Sampler and texture
import acceptance remain open; this is not full engine parity.

## Sprite Tight meshes and default physics shapes

Imported sprites now get Unity's Tight mesh, `textureRect` and default physics shape. The generator
(`crates/unbound-assets/src/sprite_mesh.rs`) reproduces the measured Editor outlines and meshes
(tests/projects/spriteimport): alpha mask grown by a Manhattan distance transform, corner-mask
Moore tracing with XOR region removal, direction-run line fitting with a one-pixel normal offset,
area-cost vertex removal that never shrinks the bounds, rect clipping, rounding, and libtess2
(vendored in `native/libtess2`) for the triangles, in the float operation order that matches the
measured outlines. UVs come back from the unit vertices; quads use the Editor's measured triangle
order.

tests/projects/spriteimport (52 sprites, two Editor captures byte-identical): 515/520 values match
exactly (baseline 241/520). The rest: a max-size-downscaled texture whose resized alpha is up to
4/255 off the Editor's (28 pixels cross the alpha-0 threshold), and the default physics shape of a
Polygon-mode sprite (Unity's mesh-edge outline, not made; warns). Resources (539/539, 62/62
images, worst mean 0.57, was 0.83: SpriteMask range images now draw the tight Circle mesh, worst
1 from 230), urp2d (13 images at 0.00) and twod (same 4 scenarios as the baseline) have no
regressions. `Sprite.Create` still builds quads.

### Root sprite integration, 2026-10-31

Merged 9f4270d7 and bumped the combined texture importer cache to version 9 in a
separate commit. Both input branches used version 8 independently; neither branch's
cached output is valid for their combination. Probe runners now require an explicit
release and fresh capture label, and the sprite comparator retains the complete
field and nested-value union outside Git with exact f32 comparisons.

Root independently confirms both Native repeats equal the single canonical reference.
The combined texture/sprite importer matches 519/520 fields across 52 sprites,
resolving the separate sprite branch's four downscale fields. At flattened-value
granularity, 7637/7654 observations pass, 5989 are resolved and none are introduced.
All 17 retained observations describe the unsupported default physics shape of a
Polygon-mode sprite. Runtime Sprite.Create Tight geometry remains open.

Asset tests pass 71/71, runtime tests 38/38 and selected managed tests 853/853.
The same full test assembly gives 1139/1151 before and after, with the same 12
legacy text/particle failures and zero skips. API-check reports zero errors in
2279 types; regenerated Core, Physics and Editor modules match. Four resource-mask
images improve; all other 2025 rendering payload files, including UIToolkit JSON
and its three PNGs, are byte-identical. Resources retain 539/539 values and 62/62
passing images, with worst mean improving from 0.83 to 0.57. All three clipping
profiles, colors and focused cameras pass unchanged. URP 2D retains 13/13 images
within its published mean limit, worst mean 0.000417; the full 2D value union retains
the same 22 differences across four scenarios with zero introductions.

The source-correct immutable build input and complete unions are cache-only. Native
API12, render protocol5, PhysX shim9 and shader cache13 remain unchanged. The next
input integration changes the capture clock's first frame and fixed unscaled time;
its physics/audio/particle regressions are a separate validation wave.

## Ordered input events within a frame

Presses and releases of keys and mouse buttons now reach every input path in the order they
happened, also when several fall between two frames. The window, remote viewers and scripted
runs (`unbound run --keys FRAME:+KEY,-KEY,...`) pass mouse buttons as ordered key events. The
Input Manager counts every change since the last frame: a click within a frame is down, up and
held for that frame, in Update and FixedUpdate alike. IMGUI events go out at
`PreUpdate.IMGUISendQueuedEvents`, before the Input System's update and Update, in order, with
the modifiers held after each event and the button's key code on mouse events; Shift, Control
and Alt are function keys. The Input System gets one state event per change, mouse buttons
among the keys. Two clock corrections came from the same Editor record: a capture rate set in
Awake steps the first frame, and each fixed step's unscaled time is the frame's unscaled time
less the step's lag behind the game clock. The Input System's fixed-update mode times events
against that clock, so events now reach the next fixed step instead of waiting indefinitely.

Validation:

- tests/projects/inputframes: two independent windowed Editor captures on private Xvfb displays
  (RTX 4060 through VirtualGL) agree on 3786/3786 compared records, and their raw records are
  identical apart from clock fields. Two later repeats with the run watchdog also match the
  published reference 3786/3786. Unbound matches 3785/3786 with nothing extra and no time
  problems. A first run of the in-progress checkpoint matched 2232; the ddb1dff1 baseline cannot play the steps, because it has no `--keys`. The missing
  record is a keyboard `DCFG` event the Editor sends before this probe's first key. Reading the
  layout in Start does not stop it, and neither does using only the Input System. The input probe
  never records it, so its cause is still unknown.
- The input (420/420), messages (316/316), playables and editorui probes give the same reports
  as the ddb1dff1 baseline. The playables report keeps the baseline's two differing scenarios.
- api-check: zero errors in 2278 hand-written types. Managed tests: 993/1005 pass. The nine
  TextTests and three ParticleSystemTests that fail also fail with the ddb1dff1 sources. The
  unbound-runtime (39) and unbound-app (18) Rust tests pass.

Commands and Editor findings: tests/projects/inputframes/README.md. Captures, scratch variants and
comparisons are in `~/.cache/unbound/validation/runs/inputframes/`.

### Texture import: the Editor's mip, fill and resize arithmetic, 2026-10-31

The remaining texture import differences (65 levels per profile, 33,614/33,614/33,513 bytes in
Gamma/Linear/Android, 72/72/73 of them correct before the float mip chain) came from the
importer's arithmetic, not from tolerances. Alpha dilation, mip halving, border mips, coverage
preservation, per-level processing, the mip fade, bilinear halving and the stb resize were
modelled against the saved 6000.0.71f1 references until every byte matched:

- Alpha is transparency is a jump flood (4 neighbours at ±k, Manhattan distance, steps
  min(max(w, h) / 2, 16) down to 1, bottom-up), not an average of neighbours; it runs whenever
  the format stores alpha. Texels the flood does not reach keep their colour.
- Preserve coverage bisects the alpha threshold against the level above (stopping within 0.5%)
  and scales alpha by reference / threshold.
- Box and Kaiser are float sums in a fixed order; a linear texture's first mip filters its
  bytes (times 1/255); sRGB uses a float table and float gamma encode; odd levels use
  16.16 fixed-point bilinear; flip green applies to each level after filtering; the fade is
  float on the encoded level; normals from height follow a float formula that matches the references.
- The Mitchell resize is stb_image_resize 0.97 on the bottom-up values (Mitchell to shrink,
  cubic B-spline otherwise, the Editor's modified Mitchell for float images, row bands per job).

The 72/72/73 regressed bytes were transparency-fill and rounding consequences of the earlier
approximations; all are now exact. On the merged integration (texture cache VERSION 10):
textureimport 484/484 levels and 1,172,940/1,172,940 bytes exact in every profile at tolerance
0, zero introduced bytes. spriteimport stays 519/520 (the open Polygon physics shape); the
`downscaled` texture's alpha now equals the Editor's pixel dump (16,384/16,384 in the model,
550 differed before). modelimport Gamma/Linear 6032/6033 and URP 7011/7768 unchanged, zero
introduced. Asset tests 71/71, runtime tests 38/38; API check zero errors in 2279 types, Physics
and Editor generated files match. Unmeasured: the HDR resize kernel and band splits whose float
mapping differs from the whole image's (none of the probe's sizes).

### Root input clock verification, 2026-10-31

Integrated ordered input at e3a7c7c2 and made its runner release-specific and
write-once at f4cadec2. The source-correct CLI/Core pair passes the existing Native
comparison at 3785/3786 records, zero extra records and zero time problems. The
single retained difference is the keyboard DCFG event. Legacy input420 and
messages316 pass. Playables retains the same two failing scenarios and editorui
the same expected height-cache difference; both raw captures are byte-identical
to the previous runtime.

The same 1153-test assembly gives 1139 passes/14 failures against the previous
Core and 1141 passes/12 failures after integration. Both new input cases pass;
the nine legacy TextTests and three ParticleSystemTests failures remain, with
zero introduced failures or skips. Selected tests pass859/859; assets71, runtime39
and app18 pass. API-check reports zero errors in2279 types; generated Core,
Physics and Editor modules match.

Clock-sensitive captures use separate private before/after CLI/Core pairs. All
nine physical and bundle-audio output files are byte-identical: physics defaults,
broad physics, joints, controller, forces, ray hits, Joint2D, effectors and audio.
The scene matrix matches5055/5055, including4803 exact and252 original rounding
observations. Particle lifetime30080, emission13604 and force-field2346 outputs
are also byte-identical; emission retains its same72 count differences. Noise23646,
streams3544 and all20 particle/stream PNGs remain unchanged. Broad particles keep
the same597 failed values, with identical failing values and zero introductions;
only four already-volatile wall-clock unscaledDeltaTime fields vary in the raw
capture. No comparison masks or tolerances changed.

The fixed-unscaled-clock relationship is measured at timeScale1; other scales
remain unmeasured by the input probe. Input artifacts remain frozen in the cache.
Native API12, render protocol5, PhysX shim9 and shader cache13 remain unchanged.
The subsequent texture-cache10 build performs the combined rendering replay.

### Texture resize: measured job-worker dependence and HDR, 2026-10-31

The texture import probe now also downscales a soft 512x512 image to 128 rows, a soft 600x360
image (no NPOT scaling) to 256x154 and a float EXR to 128 rows (RGBAFloat override, probe line as
raw floats). Two default Editor captures per profile agreed byte for byte and extend the
published reference (21 lines per profile; the earlier 559 values are unchanged). Captures with
`-job-worker-count 1` and `3` (honoured per Editor.log) differ from the default only in
MaxSizeSoftWide: 3, 1 and 1 bytes of mips 0-2 at 1 worker, 1 and 1 byte of mips 0 and 2 at 3.
The Editor splits resized rows into min(height / 16, workers) stb regions; its default worker
count is `sysconf(_SC_NPROCESSORS_ONLN)` less one, at least 1, at most 112
(15 here), not the affinity mask Unbound used. The stb model reproduces each capture only with its
own count, so Unbound now uses the Editor's count and the texture cache key includes it.

The float case confirmed the Editor's modified Mitchell kernel `(27 m(x) + 2) / 35` (plain
Mitchell misses about 55k of 65,536 values) and showed that a gamma project gamma-encodes HDR
colour after the resize; Unbound converted before it and now converts per level after the chain
(cubemaps keep converting first). Texture cache VERSION 11.

Unbound on the extended reference: 501/502 levels and 1,470,348/1,470,348 byte values exact in
Gamma, Linear and Android; the float case's line still differs (the runtime reports an imported
float texture as RGBAHalf, so the line has another format and length), while the importer's floats
equal the Editor's bit for bit in every profile (unbound-assets test
`hdr_max_size_resize_matches_the_editor`). Sprite import stays 519/520; asset tests 72/72.

### Imported HDR format and combined texture validation, 2026-10-31

Explicit RGBAFloat platform overrides now retain the existing float32 format through
runtime import descriptors and Texture2D/Cubemap construction. Default and explicit
RGBAHalf imports keep their previous format. This uses the existing render format
values; packet layouts, Native API12, render protocol5, PhysX shim9 and shader cache13
are unchanged. Texture cache11 and its measured worker-count key remain in use.

Root replay against the extended Unity6000.0.71f1 reference passes502/502 mip levels
and1,732,492/1,732,492 pixel bytes in each of Gamma, Linear and Android. Every complete
580-record capture is byte-identical to its canonical reference and both independent
Editor captures. This closes the prior501/502 descriptor/length difference. The
8-bit Gamma/Linear cases agree; the HDR first level retains its measured project
color-space difference.

Assets72, runtime39, app18 and selected managed859 tests pass. The same1153-test
assembly preserves1141 passes and12 inherited failures before/after this format
change. API-check reports0 errors in2279 types; generated Core, Physics and Editor
modules match exactly. Combined particle and rendering replays are recorded
separately; this result certifies the measured texture import matrix.


### Particle count integration, 2026-10-31

The measured simulation remainder and per-subdivision emission accumulator are
integrated with the current texture, input, cloning and animation changes.
Root's private before/after recordings resolve all72 original count differences:
the original13604 emission observations and30080 lifetime observations pass.
Force fields2346, noise23646 and streams3544 pass, all20 particle/stream PNG
controls remain pixel-identical, and the broad physical capture is byte-identical.
Broad particles improve1448/1560 to1453/1560 records, resolving38 scalar values
and retaining559 unchanged failures with zero introductions.

The complete expanded emission reference remains strict at15721/16573 passing
records and5997 failing scalar values. Missing parent values are expanded when
comparing every nested leaf, so newly present particle fields are retained in the
full union rather than hidden. This comparison introduces zero failing leaves.
Three previously exact lifetime values differ by one float ULP after integration;
all remain within the unchanged2e-4 lifetime tolerance and are listed separately.
The375 subdivision position/velocity/lifetime controls remain exact. Expanded
seed, lifetime and stopped-simulation gaps are still open.

The same1191-test assembly improves1162 to1180 passes:18 failures resolve,
11 inherited failures remain, zero introductions and skips. API-check reports
zero errors across2279 types and generated Core, Physics and Editor modules
match exactly. All run outputs, complete unions and immutable matching-artifact
manifests stay in the validation cache. Combined rendering replay is separate.
Native API12, render protocol5, PhysX shim9 and shader cache13 are unchanged.

### Texture arrays, volumes and their asset files, 2026-10-31

New probe tests/projects/texturearrays: 25 imported flipbooks (Texture2DArray, Texture3D and Cube
shapes: cell order, NPOT and max size, sRGB, box/Kaiser, fade with border, coverage with alpha is
transparency, a normal map, a 3-column 100-texel image, filter and wrapU/V/W, unreadable) and 10
Texture3D/Texture2DArray/CubemapArray `.asset` files (RGBA32 with and without mips, sRGB and
linear, RGBAHalf, R8, unreadable). Each value is the info line, GetPixels32, GetPixels and
GetPixelData of every slice and mip, plus 11 point-sampled GPU readbacks. Two independent Editor
captures per profile (Gamma, Linear, Android) agree byte for byte and are the published reference.

Measured: slices are the cells top-left first; every slice is a 2D texture of the cell size
(NPOT, max size and the 2D mip chain per cell); alpha is transparency fills only the first
cell-sized run of the whole bottom-up flipbook, and a slice's first border mip reads its edge
columns from the flipbook with the cell width as the stride (both modelled texel for texel); a
volume halves x, then y, then z with truncating byte means, ignoring sRGB and the mip filter; a
Cube shape clamps on all axes; GetPixels reads bytes as b * (1/255); R8 reads (r, 1, 1, 1); asset
files store a GraphicsFormat with `m_ColorSpace`. Texture cache VERSION 12.

Unbound: every info line, 556/556 GetPixels, 520/520 GetPixels32 and 520 of 556 GetPixelData values
(2,100,272, 510,740 and 511,188 bytes) exact in all three profiles; the 36 others are the imported
cubes' `Cubemap.GetPixelData`, still a stub. GPU samples 9/11: the two beyond the depth of a
wrapW-Clamp volume repeat, because the renderer's samplers have no W wrap mode. Regressions:
textureimport 502/502 mips and 1,732,492/1,732,492 bytes exact in Gamma, Linear and Android;
spriteimport 519/520; modelimport Gamma/Linear 6032/6033 and URP 7011/7687 (complete union 7768
keys, the same 757 failures line for line); descriptions 0 differences; unbound-assets 73/73 (new
`flipbook_arrays_and_volumes_match_the_editor`); api-check 0 errors in 2279 types.

Follow-up the same day: the probe adds a 4x4x16 imported volume and a 4x4 array of 16 slices,
point-sampled at levels 1, 2 and 4 (new independent Editor pairs agree byte for byte; every earlier
reference line is unchanged). The Editor reports 5 levels for the volume and 3 for the array.
Unbound's renderer capped a volume's GPU mip count by width and height only, so the volume's
level-4 sample read level 2; the count now includes the depth for volumes only. Unbound: 37/37
info lines, 609/609 GetPixels, 573/573 GetPixels32 and 573/609 GetPixelData values exact (the 36
others the imported cubes' stub), GPU samples 14/16 (the two wrapW-Clamp ones), in all three
profiles. textureimport, spriteimport and modelimport (Gamma, Linear, URP failures line for line)
unchanged; unbound-assets 73/73; api-check 0 errors in 2279 types. ImportedTextureInfo's
documented dimensions now include 3 and 5 (layout unchanged).

Combined with the later physics, audio and sampler-property work (2026-11-01): the texture array
captures are byte-identical to the earlier ones in all three profiles (the same 38 differences:
36 imported-cube `GetPixelData` values and 2 wrapW-Clamp samples). textureimport (502/502 mips,
1,732,492/1,732,492 bytes in each profile), spriteimport (519/520) and modelimport (Gamma and
Linear 6032/6033, URP 7011/7687 with 550 missing, 126 differing and 81 extra keys, union 7768;
descriptions 0 differences) captures are byte-identical to those of the runtime without the
texture array work. unbound-assets 73/73; api-check 0 errors in 2289 types; regenerating the
compatibility stubs changes nothing. Texture cache VERSION 12 (13 since the storage corrections below); native and render protocol
versions and texture packet layouts unchanged.

### Texture2D GetPixels byte conversion, 2026-11-01

New probe tests/projects/texturepixels (on the texture array fixture): 16x16 textures of RGBA32,
ARGB32, BGRA32, RGB24, Alpha8, R8, RG16, R16, RGBA4444, ARGB4444 and RGB565, sRGB and linear, with
explicit mips, and a `LoadImage` result, every byte position running through 0..255. Two
independent Editor captures per profile (Gamma, Linear) agree byte for byte. `GetPixels` (every
overload and level) reads a byte b as b * (1 / 255) in single precision; `GetPixel` and Alpha8's
`GetPixels` read b / 255. The two differ in the last bit for 126 of the 256 byte values (51 gives
0.200000018 against 0.2). Unbound's Texture2D `GetPixels` now multiplies for byte formats other
than Alpha8; `GetPixel`, Color32, `EncodeToEXR` and `RenderToCubemap` keep dividing.

Unbound on the probe: 136 differences in Gamma and 138 in Linear before, 103 and 105 after (33
resolved, none introduced). Every `GetPixels` value of RGBA32, ARGB32, BGRA32, RGB24, the linear
and mipmapped RGBA32 and the loaded PNG is now exact. Remaining, unchanged: `GetPixelData` of
script-made textures throws (27 values), the graphicsFormat of ARGB32 and Alpha8 (and in Linear
of R8 and RG16) and the loaded PNG's format, R8 reading (r, 0, 0, 1) instead of (r, 1, 1, 1), Alpha8's `GetPixels32`
(1, 1, 1, a) instead of (0, 0, 0, a), `SetPixelData` of RG16, R16, RGB565 and RGBA/ARGB4444
(grey texels), `GetPixelBilinear` at texel centres (23 values) and `GetPixel` of those R8 and
`SetPixelData` cases (11 values). tests/projects/resources at its 2e-3 tolerance is unchanged (501/539 values, 62/62
images); exact float bits of its 208 Texture2D mip values go from 149 to 176 (none lost).
Managed tests 1264/1275 (the 11 failures of the runtime before, 4 new tests pass).

### Layered texture storage corrections, 2026-11-01

The texture pixel probe adds script-made volumes, BGRA32 array and volume `.asset` files, an
imported readable PNG encoded to EXR as its first read and GPU draws of the fixture's imported
readable cubemaps before and after `Apply(false)`; a new pair of independent Editor captures per
profile agrees byte for byte and keeps every earlier reference line. Measured: `Texture3D.GetPixel`
reads b / 255 (its `GetPixels` b * (1 / 255)) and `SetPixel` rounds halves up; B8G8R8A8 assets hold
B, G, R, A bytes and report BGRA32; EXR encoding succeeds as the first access; `Apply(false)`
leaves an imported cubemap's GPU faces unchanged.

Corrections: R and RG float arrays, volumes and cube arrays read one in the channels they do not
store (12 resources values had read zero); layered sizes are computed in 64 bits and a texture
larger than one array fails with its size instead of wrapping; EXR encoding fetches an imported
texture's pixels; an imported readable cubemap's `Apply` fetches its faces and uploads them in the
import's row order; B8G8R8A8 assets read as BGRA32 bytes; `Texture3D.GetPixel`/`SetPixel` touch
one texel (b / 255, halves rounded up); single-cell flipbook arrays and volumes stay uncompressed
like the others, because the renderer binds them through RGBA8 views (texture cache VERSION 13).

Against the runtime without the texture array work: the texture pixel probe goes from 240 to 146
differences in Gamma and 242 to 148 in Linear (94 resolved, none introduced). Remaining there,
unchanged: the Texture2D gaps above, `GetPixelData` of the imported PNG, `GraphicsFormatUtility.
GetTextureFormat(B8G8R8A8_*)` returning ARGB32 instead of BGRA32, the imported cubemaps' GPU faces
drawn upside down already before any CPU access (36 values: the import upload's row order), and
one-ulp RGFloat differences that come from the probe's own float arithmetic in the Editor. The
texture array probe, which that runtime could not run (it stopped with an exception before
writing), keeps the earlier 37/37, 609/609, 573/573, 573/609 and 14/16 values in every profile.
tests/projects/resources is identical to that runtime's (513/539 values, 62/62 images, every
image byte for byte); exact float bits rise from 472 to 499 of its 540 keys (the 27 Texture2D
`GetPixels` mip values), none lost. textureimport, spriteimport and modelimport captures are
byte-identical to it. Managed tests 1275/1286 with the same 11 failures; unbound-assets 74/74;
api-check 0 errors in 2289 types; regenerating the compatibility stubs changes nothing.

### Texture W wrap mode, 2026-11-01

NativeApi 14 appends `wrap_w` to `TexturePacket` and `ImportedTextureInfo`, and the
`texture-sampling-properties` call carries it (filter, wrap U, V, W, anisotropy): a texture's own
sampler uses it as its W address mode, which only a Texture3D's samples read. Imported textures
report the importer's `wrapW` from the import info. Inline, default, integer, compute, grab, video
and built-in lighting samplers keep repeating along W; no parity is claimed for them. A 13 side and
a 14 side refuse each other at startup in either pairing. Render protocol, shader cache and texture
cache are unchanged (texture cache 13 already stored `wrapW`).

tests/projects/texturepixels adds one bound 4x4x4 volume drawn beyond its depth while its
`wrapModeW` goes Repeat, Clamp, Repeat, then Clamp with an `Apply` and Repeat again (a new pair of
independent Editor captures per profile agrees byte for byte; every earlier line unchanged).
Against the runtime without the change: the texture array probe resolves its two wrapW-Clamp GPU
samples in each profile (38 to 36 differences, the 36 imported-cube `GetPixelData` values remain),
the texture pixel probe its four live Clamp samples (150 to 146 in Gamma, 152 to 148 in Linear),
none introduced. textureimport and resources (539/539, 62/62 images) are byte-identical; rtdepth
probe (1071 values, 316/368 images) and controls (46 values, 13/17 images), materials (1004) and
graphics (80) keep every status. Managed tests 1289/1300 with the same 11 failures; unbound-assets
77, unbound-runtime 50, unbound-app 20; api-check 0 errors in 2290 types; regenerated stubs
unchanged.

### HDR and count rendering preservation, 2026-10-31

The matched count/HDR runtime at674c8eae completes the unchanged strict checkers
for graphics, resources, built-in pipelines/effects, three clipping matrices,
both Canvas color profiles, both focused camera profiles and UI Toolkit.
All2029 captured payload files are byte-identical to the prior sprite/cloning
runtime, including every JSON, pixel buffer and PNG. UI Toolkit's three renders
and11 interactions are unchanged. Existing expected Native differences remain
in the full reports; no comparison tolerance or mask changed.

This preservation result uses shader cache13. The subsequent sampler14 and its
new border/cache controls have separate acceptance reports.

### Humanoid avatars and HumanPoseHandler, 2026-10-31

Humanoid avatars are built from a model's hierarchy and the human description its meta carries
(bone mapping, T-pose skeleton, limits, twist/stretch parameters), or by AvatarBuilder from a
scene object: the human skeleton, muscle axes per bone, masses, body frame and scale, as the
Editor computes them. HumanPoseHandler reads and writes poses through muscle space (retargeting
with limb alignment, twist distribution, fingers). The HumanTrait tables, Avatar and Animator
humanoid members and AvatarBuilder's validation errors follow. An animated FBX's nodes now rest
in their pose at time 0, as the Editor imports them.

Validation:

- tests/projects/humanoid: two Editor captures are identical. Unbound matches every avatar,
  trait, Animator, AvatarBuilder, rest-pose and HumanPoseHandler value (55820/55820 for the
  handler: the default pose, every muscle alone at -1/1, random and out-of-range poses on both
  rigs). Clip properties, playback, retargeting in the Animator and the IK pass are next.
- animator 2236/2236, animatoradditive 1100/1100, bundle animation 8/8; modelimport Gamma and
  Linear 6032/6033, URP 7011 matching as before. Asset (71) and runtime (39) Rust tests, managed
  animation tests (32) pass; api-check: zero errors in 2287 hand-written types.

### Humanoid clips, root motion, retargeting and IK, 2026-10-31

A Humanoid model's clips become muscle clips of its avatar when first bound: each frame's
skeleton pose is read into muscle space as the importer does (root, IK goals, muscles; rotation
curves kept in one hemisphere, body muscles unwrapped and offset by whole turns, spline slopes).
Muscle curves, from models and from `.anim` files, drive the Animator's human pose, which is
retargeted onto the skeleton after the layers are written, so any humanoid clip plays on any
humanoid avatar. The body's motion becomes root motion (yaw and the full translation for default
clip settings, loop cycles, the feet pivot), which the importer turns on for humanoid models;
clip averages follow. Layers with an IK pass send OnAnimatorIK, then the full-body IK solve
applies the goals, hints, look-at and body position/rotation set there.

Hooks in the generic Animator code: `AnimatorPlayer.Evaluate` calls `Animator.ApplyHumanPose()`
after `PoseWriter.WriteLayers`; `AnimatorPlayer.ComputeRootMotion` hands humanoid clips to the
humanoid root motion; `Binding` binds muscle curves to the Animator; layers carry `m_IKPass`.
OnAnimatorIK runs inside the Animator's evaluation (no player loop change).

Validation:

- tests/projects/humanoid (two identical Editor captures): Unbound matches 120564/120650 values.
  Avatars, traits, Animator members, AvatarBuilder, HumanPoseHandler, clip properties, playback of
  both rigs (12238/12238, 7366/7366) and retargeting both ways match fully; the IK pass matches
  every transform, body and goal value. The 86 remaining values are muscles 41 and 43 (left
  upper arm and forearm twist) read back from the IK pose with HumanPoseHandler: their sum
  matches, the split differs by whole steps of the twist alignment's iteration, which the IK
  pose's last-bit differences flip.
- animator 2236/2236, animatoradditive 1100/1100, bundle animation 8/8; modelimport Gamma and
  Linear 6032/6033, URP 7011 matching as before. Asset (71) and runtime (39) Rust tests, managed
  animation tests (32) pass; api-check: zero errors in 2288 hand-written types.


## Automatic sampler property updates (2026-10-31)

- Source `9e8ed0f3` was rebuilt with matching CLI and managed outputs. All seven sampler
  profiles pass 371166/371166 observations without increasing any previously accepted
  numeric error. The new cached-material control passes 171/171 and resolves 33 differences,
  with zero introduced, retained or worsened accepted values. Both independent Unity
  6000.0.71f1 captures agree, including every earlier control value.
- Filter, wrap and anisotropy setters submit sampler metadata through the existing JSON
  control route and share the texture upload generation counter. They preserve dirty CPU
  mip data until Apply and preserve RenderTexture storage. No packet layout changed;
  NativeApi12/shim9/shader14 remain in use.
- Combined asset cloning passes 14332/14340 observations. The 45 clone sampler differences
  are resolved. Eight inherited source metadata/upload differences remain, and each separate
  color profile retains four Alpha8 graphics-format differences. Mixed Animator control
  passes 1100/1100 records and 13200/13200 values.
- The proposed collapsed-border guard was rejected by two agreeing Unity captures. It
  introduced 668 failing channels and worsened 674 channels in the added control. Production
  retains the measured corner behavior; complete rejected comparisons remain in cache.
- Probe sources, canonical references and commands are in `tests/projects/samplerbias`,
  `tests/projects/assetclones` and `tests/projects/uitkpixels`. Full unions and build/test/API
  reports are cache-only. The later humanoid/audio/clock integration requires its own replay.


## Combined humanoid, audio, clock and sampler replay (2026-10-31)

Validated production source `5e226544` includes the pinned humanoid stage, mixer filters,
AudioRenderer safety corrections, exact-duration capture clocks and automatic sampler metadata.
The matching CLI, Core and actual freshly built PhysX library were copied from this build.
All 33 runtime artifact hashes remain unchanged after the complete replay. NativeApi12,
PhysX shim 9 and shader cache 14 remain in use.

- Normal-stub builds pass. Full managed differential: 1208 tests, 1180 old passes preserved,
  17 resolved, 11 inherited failures retained, zero introduced or skipped. The retained failures
  are nine Text tests and two ParticleSystem tests. Rust assets 72/runtime 46/app 18 and 33 portable
  shader tests pass; one inherited private shader fixture remains excluded. Separate no-stubs
  API verification reports zero errors across 2289 types. Core, Physics, Editor Core, Audio
  and Animation regenerated modules match the committed generated sources byte for byte.
- All seven audio profiles pass 11516039 values with the original tolerances, and every capture
  file is byte-identical to the accepted delivery. Capture registrations are consumed on every
  render attempt, NativeArray lifetimes are checked before copying, audio callbacks cannot
  re-enter renderer RPCs, and capture start waits for the device block and clock update.
  The handoff lock has a concurrency unit test; actual-device recording remains unmeasured.
- Five independently agreeing startup clock profiles match 120/120 fields exactly and resolve
  44 prior differences. They include timeAsDouble, timeScale 0.5/1.5, and a three-second capture
  duration whose integer FPS view is zero. Captured time accumulates whole clock ticks before
  conversion to public float/double values. Existing fixed-step/unscaled-time behavior is
  preserved: legacy Input 420/420 and inputframes 3785/3786 retain only the prior DCFG event,
  with zero extra records or time problems. Messages 316 and Canvas lifecycle 8671 pass.
- All sampler profiles and cache controls pass 371337/371337 observations without numerically
  worsening accepted values. One sampler startup failed with Device(Lost) before any capture;
  a fresh-directory retry passed. The failed attempt remains in cache.
- The actual current PhysX library passes 11958 records, including matrix 5055/5055 and all
  force/controller/ray-hit and focused lifecycle gates. Representative audio-import/physics/
  graphics unions introduce nothing and retain the same 5721/45/0 failures, respectively.
- Broad particles preserve 1453/1560 records and the same 559 scalar failures. Lifetime 30080,
  force-field 2346, noise 23646 and streams 3544 pass, and all 20 PNG files are byte-identical.
  The complete unfiltered before/after report retains four wall-clock changes and one game-time
  scalar corrected from 0.100000009 to the Native 0.1 value. Every retained failing value is unchanged.
- Humanoid remains 120564/120650 with the same 86 twist values; mixed layers 1100/1100 pass.
  All three transition oracles pass (253/831,718/3428,110/132 samples/callbacks). Expanded UI
  JSON matches exactly; original RGB residuals 22473/22517/30011 and alpha remain unchanged,
  with zero introduced or worsened channels. All 2029 render payloads are byte-identical to
  the accepted sampler baseline.

Source probes and canonical release references remain under tests/projects. Complete commands,
artifact receipts, original failures, fresh retries and full unions are cache-only. The initial
ad-hoc driver failures are preserved, including missing native test-library packaging and
incorrect lifecycle runner configuration. No frozen baseline was changed. This closes this
integration wave, not full engine, rendering, audio-effect or older-release parity.


## AudioMixer Echo integration (2026-11-01)

Echo delivery `197656eb` is integrated, with the reviewed allocation correction in production
source `02e061b5`. Independent Native repeats agree at 48 kHz and 44.1 kHz. The root replay passes
24379636/24379636 values with unchanged ATOL 2e-5/RTOL 2e-4. All 70 device PCM files are bit-identical
to the canonical Unity 6000.0.71f1 captures, and every output file is byte-identical to the
accepted Echo delivery. Complete float delta reports preserve 681966/686797 tolerated patterns
at the two rates; their maximum absolute difference from Native is 5.820766091346741e-11.

- The persistent stereo delay uses the measured eight-frame quantization and signed 16-bit
  history, with independent Drymix/Wetmix and feedback Decay. Defaults, finite parameter
  endpoints, fractional delays, source completion/tails, effect/group bypass, serialized effect
  order, parent routing, summed voices, attenuation, exposed values and immediate snapshots
  are covered by the 35-case matrix at both rates.
- Independent review found a malformed-rate allocation risk. Histories now have an explicit
  8 MiB backend limit and use fallible allocation. Group configuration and rate resets validate
  the limit before mutation. Tests reject int.MaxValue sample rates at default and maximum
  delay and preserve the prior configuration; a subsequent valid reset succeeds. A five-second
  history at 384 kHz fits the resource budget, without claiming unmeasured rate parity.
- Both normal-stub and no-stubs builds pass. API verification reports zero errors in 2289 types;
  all five generated module comparisons are byte-identical. Full managed differential preserves
  all 1197 prior passing tests and resolves the new Echo configuration test: 1198/1209 pass,
  with 11 unchanged Text/Particle failures, zero introduced and zero skipped. Rust audio 38,
  runtime 49, assets 72, app 18 and portable shaders 33 pass; the same private shader fixture is excluded.
- All 11516039 previously accepted mixerfilter values pass and their files remain byte-identical.
  The 120 exact capture-clock fields pass. Audio-import/physics/graphics retain 5721/45/0 failures,
  with zero introduced values or raw changes. The actual built native library matches the
  prior root library hash; none was replaced with a frozen sibling artifact.

The 33 matching runtime files retain their original hashes after replay. Canonical references
and portable commands are in tests/projects/mixerecho; full comparisons, numerical deltas,
review evidence and runtime receipts remain cache-only. Auto-suspend, surround, clipping beyond
the fixture amplitudes, running-tail changes, timed snapshots and other releases remain outside
this certification. Global slot wet mixing and other effects remain unsupported. This establishes
only the measured Echo scope, not complete audio or engine parity.

## Model importer clip settings

Clips a model's `.meta` defines (`clipAnimations`) now import and play as the Editor makes them,
for generic rigs: each clip's ID is its `internalID`; a range is resampled from its first frame
(also between frames) and stops at the take's end; every animated node gets position, rotation and
scale curves, constant ones too; animation compression Off keeps a key at the start, on every take
frame and at the end, rotations and scales with smooth tangents and positions linear. Transform
masks remove the curves of the transforms they leave out (from the model's list, or from the
`maskSource` AvatarMask, not its copy in the meta). An additive reference frame inside the range
refers to the clip itself, one outside it to the take's hidden `__preview__<take>` clip, which
every take now has (NotEditable and HideInHierarchy: listed by `LoadAllAssetsAtPath`, not by
`Resources.LoadAll` or `LoadAllAssetRepresentationsAtPath`). Parameter curves become float curves
on the root's Animator; clip curves drive float parameters only. Mirror, wrap mode and the root
settings are kept in the clip settings. With animation import the model rests in the first take's
pose at its start. The model cache format is 8.

At play time, loop pose adds `(start − stop) × phase` to float values and turns rotations by
`q · normalize(phase · d.xyz, d.w)`, `d = stop⁻¹ · start`; it and the clip's and state's cycle
offsets act only on looping clips, also for events and root motion. An `Animator.Update(0)` that
enters no state still fires the events at the start time, once, and state times are compared with
event times in single precision.

Validation:

- tests/projects/modelclips: two independent GPU Editor captures are byte-identical (2949
  records); Unbound matches 2949/2949 records, 31391/31391 values (the ddb1dff1-based baseline
  matched 514 of an earlier 2641-record version: clip IDs were not the meta's).
- modelimport Gamma and Linear 6032/6033, URP 7011 matching with the same missing/differing/extra
  counts as before, descriptions 35/0; animatoradditive 1100/1100; animator 2236/2236; animation
  from bundles 8/8. api-check: 0 errors in 2279 types. unbound-assets (74) and unbound-runtime
  (39) tests pass. Managed tests: the nine TextTests fail as before, and 21 PhysicsTests cases
  (ClonedBodies*, SerializedInactiveBody*) fail with these sources and with the base sources alike.

Not covered: Unity's keyframe reduction for the reducing compression modes (approximated by
ufbx's near-lossless reduction with linear tangents), humanoid clips (mirror, masks by body part),
and root-motion nodes and Bake Into Pose (next).

## Generic root nodes, Bake Into Pose and target matching

A generic avatar's root node (`motionNodeName`, with an avatar) now gives every imported clip
MotionT/MotionQ curves on the Animator: the node's pose relative to the model's root at the clip's
frames. Root motion follows them as it follows root curves; the node keeps its own curves.
Bake Into Pose and the related settings (based upon original, height from feet, orientation
offset, level) change nothing at play time in the Editor, for root nodes and root curves alike;
they zero the baked components of `averageSpeed`. `averageSpeed`, `averageAngularSpeed`,
`apparentSpeed` and `averageDuration` come from the motion curves (twenty equal parts of the clip,
each in the node's frame at its start); `hasMotionCurves`, `hasMotionFloatCurves` and
`hasRootCurves` read the curves. `MatchTarget` matches a generic rig's root (AvatarTarget.Root):
each step adds its share of the remaining correction, the target less where the clip's motion
alone would bring the root by the end, with rotations by the normalized lerp; the step past the
end completes it. Body-part targets are left to humanoid avatars (ignored with a warning).

Validation:

- tests/projects/modelroot: two independent GPU Editor captures are byte-identical (3714 records);
  Unbound matched 3699/3714 records, 35781/35796 values; the 15 others were `apparentSpeed` of
  the clips that turn about the up axis (see below for the merged result).

## Model clip settings and generic root nodes with humanoid avatars, 2026-11-01

The generic model clip and root-node work now sits beside the humanoid avatars, muscle clips and
humanoid root motion. Both paths read the same parsed clip settings. With animation import a
model's nodes rest in the first take's pose at its start; without it they keep the file's static
transforms. The humanoid probe's takes start at 0, so this matches both probes. Clip documents
carry a mask asset and a Humanoid model's avatar. A clip is human only with root, goal or muscle
curves; MotionT/MotionQ alone are a generic root node's motion. A Humanoid model's root node
(`motionNodeName`) is ignored with a warning. `MatchTarget` on a humanoid avatar warns and does
nothing. Humanoid clips now get every root setting of the meta; any setting other than the
default still throws, as before. Model clips cropped from their take or masked by transforms
carry an import warning, because their muscle curves are unmeasured. The motion statistics use the humanoid motion for muscle clips and the root-node curves
for the rest. `apparentSpeed` is `|averageSpeed| · (1 + (averageAngularSpeed / 2)²)` for both:
this formula gives all 102 Editor clip values of the root-node probe exactly. The model cache
format is 11.

Validation (candidate against the Root runtime it was merged into, same commands):

- tests/projects/modelclips 2949/2949 records, 31391/31391 values (Root: 585/2949);
  tests/projects/modelroot 3714/3714 records, 35796/35796 values (Root: 764/3714).
- tests/projects/humanoid 120574/120650 values (Root: 120570/120650). In both, every difference
  is muscle 41 or 43 of the IK pose read back with HumanPoseHandler, the known twist-split
  residual. Its count changes with last-bit differences of the IK pose.
- modelimport Gamma and Linear 6032/6033, URP 7011/7687 (550 missing, 126 differing, 81 extra),
  descriptions 35/0; animatoradditive 1100/1100 (13200/13200 values); animator 2236/2236;
  animation from bundles 8/8. These equal Root's.
- api-check: 0 errors in 2290 hand-written types. unbound-assets 75 and unbound-runtime 49 tests,
  and 31 managed animation/animator/humanoid tests, pass.

## Combined Core and physics review follow-up, 2026-11-01

The integration at `691f0ed6` includes shared rounded-tip and collapsed-core
contacts, callback traversal after pair removal, first-access project settings,
and strict reporting of newly missing observations. Its 4,700-field Editor
control passes 4,570 observations, resolves 562 and introduces none; 130
`Collider2D.Distance` normal and witness-point differences remain. Broad physics
passes 11,626/11,626, motion 3,364,863/3,364,863, and the scene-settings matrix
5,055/5,055 with its original rounding bounds. Managed tests pass 1,265/1,276:
48 differences from the accepted parent are resolved, the same eleven failures
remain, and none are introduced. API checking has zero errors in 2,289 types.

All scheduled regression groups finished, including animation, input, clock,
particles, UI and rendering. Their raw deltas are retained. The broad particles
suite keeps the same 559 failing values. One trails pixel changes RGB error
four to five, below the unchanged threshold of eight. Full integration
acceptance remains open: humanoid IK readback resolves sixteen values, retains
seventy and introduces ten against the prior accepted capture. Fewer total
differences does not make those ten acceptable; they are assigned to the
humanoid follow-up. No full Core, math or engine parity is claimed.

Generic model target-matching review has a separate expanded Editor reference:
11,536 records, with the old 3,714-record prefix unchanged. The source-matched
candidate passes all 91,088 expanded values, including lifecycle, overrides,
callbacks, Rebind and single-precision normalized playback time. The full
managed differential keeps the same eleven failures and introduces none;
API checking has zero errors in 2,290 types. Model clips, additive layers,
Animator and bundled animation pass their affected controls. The model
review package remains unaccepted: humanoid IK readback resolves 24 values,
retains 62 and introduces eight against the prior accepted capture. The
complete before/after union is retained outside Git.

## Render texture elements combined integration, 2026-11-01

NativeApi 13 and render protocol 6 are integrated with typed depth, signed and
unsigned sampled bindings. Shader cache 16 also separates integer and
depth-comparison `_MainTex` slots from the filtered float layout. Sampler JSON
metadata requests validate reserved handle bits before reading or mutating
state. Texture packet layouts, PhysX shim 9 and texture cache 11 are unchanged.

The matching combined runtime passes 1,071/1,071 main RT values and 316/368
images, plus 46/46 control values and 13/17 images. The complete unions introduce
no failures or missing fields against the revised delivery. Screen encoding and
the unsupported integer-array default account for the retained image differences.
The typed `_MainTex` GPU-validation reproduction and six reserved-bit RPC
controls pass. Main, bilinear, live and cache sampler controls pass 150,053
observations with no numerical worsening of accepted values. Six affected
rendering controls preserve their previous complete comparator reports.

Managed tests pass 1,273/1,284 with the same eleven failures, zero introduced and
zero skips. Rust runtime 50, app 19 and portable shader 35 tests pass; the private
shader fixture remains explicitly excluded. API checking reports zero errors in
2,290 types, with the affected generated modules unchanged after regeneration.
The separate humanoid IK integration blocker remains open. RT acceptance does
not imply full animation, rendering or engine parity.

## Layered texture import combined integration, 2026-11-01

The revised array, volume and serialized texture readers are integrated beside
NativeApi 13 / render 6 / shader cache 16. Texture cache 13 prevents reuse of
entries produced before the shape-specific compression correction. Layered
storage checks texel, byte, upload and offset sizes before allocating or exposing
raw views; cube-face counts cannot wrap before those checks.

All three exact array comparisons preserve the revised delivery's results:
37/37 info, 609/609 GetPixels, 573/573 GetPixels32, 573/609 raw reads and 14/16 GPU
samples. The 38 retained fields per profile are the 36 imported cubemap raw
reads and two WrapW samples. Pixel controls retain 146 Gamma and 148 Linear
differences without introductions or newly missing fields. Texture import passes
all 502 mips and 1,732,492 bytes per profile, at tolerance zero. Resources pass
539/539 values and 62/62 images with the authored mip bundle staged; the sprite
probe retains its one polygon-physics difference at 519/520. RT controls retain
their previous complete result, without new failures or missing fields.

The matching normal-stub build passes 1,289/1,300 managed tests, keeping the same
eleven failures with none introduced and zero skips. The new oversized-storage
control passes without large allocations. Rust assets 77, runtime 50 and app 20
tests pass; API checking has zero errors in 2,290 types and affected generated
modules match regeneration. No new Editor captures or unrelated physics, audio
or full rendering suites were needed. Known texture gaps and the separate
humanoid IK integration blocker remain open.

## Texture W wrapping Root integration, 2026-11-01

NativeApi 14 is integrated with render protocol 6, shader cache 16, PhysX shim 9
and texture cache 13. The five sampler values are filter, U, V, W and anisotropy;
the existing shared texture generation and bind-group invalidation are retained.
The Root build uses identical Core DLL bytes for the full managed test run and
the packaged runtime: 1,289/1,300 tests pass, the same eleven failures remain,
and none are introduced. Runtime 50 and app 20 Rust tests pass. API checking has
zero errors in 2,290 types and the affected generated modules match regeneration.

The two Native live-W repeats match the published reference in each color
profile, retaining all 300 previous records and adding eleven controls. Root's
matching runtime passes all eleven live changes and reuploads in both profiles;
their complete pixel unions retain 146 Gamma and 148 Linear differences with
no introductions or newly missing values. The Gamma array replay resolves both
W samples and retains only the 36 imported cubemap raw-read differences. All
seven sampler RPC guards pass, including rejection of the obsolete four-value
request. Broader source-identical delivery results were reused; no new Editor,
full physics, audio or full-rendering runs were needed. Other sampler W policies
and the separate humanoid IK blocker remain outside this acceptance.


## Mixer suspension and initial cube upload review, 2026-11-01

Root integrates the measured mixer-suspension delivery and fixes two review
findings. Clipless callback sources participate in routed mixer activity, so a
quiet generator stays awake and late attachment wakes a suspended route. RMS
observation closes each configured DSP window even when a remote packet ends
partway through it. Both focused checks and all 57 runtime tests pass. The normal
managed build passes 1,290/1,301 tests, with the same eleven inherited failures
and zero skips. Its tested and packaged Core bytes match. API checking reports
zero errors in 2,290 types; the affected generated modules match regeneration.

The short generator profile has two complete, byte-identical Native recordings.
Its full Native/accepted-Root/candidate union has zero introduced observations,
67,584 resolved and 104,448 retained. Those counts are PCM sample observations,
not independent test cases. The retained source-callback phase and pre-activation
sink differences remain explicit. A separate union against the unaccepted R6
candidate retains its 4,096 newly failing device samples when output returns;
those same samples already fail in the accepted Root baseline. No sample shifting,
fixture quantization or tolerance changes were used. The original large suspension
matrices were not replayed after these targeted review fixes; their delivery
reports and unresolved reset/snapshot observations remain available separately.

Imported cube faces reverse their mip rows only in the initial GPU upload buffer.
Cached CPU pixels and managed Apply uploads are unchanged. Root's complete pixel
captures are byte-identical to the dedicated fix delivery: all twelve initial
samples pass per color profile, twelve previous differences resolve in each,
134 Gamma and 136 Linear differences remain, with zero introduced or newly
missing values. The 24 post-Apply sample differences per profile remain open.
Resources pass 539/539 values and 62 images; built-in RP retains its existing
124 expected value differences and all 33 images pass. All 95 image files are
unchanged from the accepted controls. NativeApi 14, render 6, shader cache 16,
PhysX shim 9 and texture cache 13 are unchanged. No unrelated physics, particle,
animation or full-rendering replay was needed; frozen baseline artifacts remain
untouched.

## Texture2D and Cubemap CPU pixels, 2026-11-01

Texture2D keeps every stored format's texels exactly and reads them as the Editor does
(tests/projects/texturepixels README): RG16, R16, RGBA4444, ARGB4444 and RGB565 data set
through `SetPixelData` no longer reads grey; R8 and RG16 fill the channels they lack with one
(255 in `GetPixels32`), Alpha8's `GetPixels32` is (0, 0, 0, a); 4-bit channels, RGB565 and R16
use their own scales. `GetPixelBilinear` mixes the four GetPixel values around (u w, v h) in
single precision. `Texture2D.GetPixelData` and `Cubemap.GetPixelData` return views of the
stored bytes that stay coherent with the pixel setters. A PNG loads as ARGB32; ARGB32, Alpha8,
R8 and RG16 report the Editor's graphics formats. Applying an imported readable cubemap
uploads its faces top row first.

On Root's frozen CLI and native library with the new managed engine: texturepixels goes from
146 Gamma and 148 Linear differences to 17 in each profile, none introduced. The 17 are the 12
initial imported-cube GPU samples before any CPU access (Root's import upload), two
`GraphicsFormatUtility.GetTextureFormat(B8G8R8A8_*)` table values, and three Texture3D RGFloat
values whose inputs the Editor's script runtime computes in double precision (`i / 37f - 2f`;
the stored values match the script's inputs in both engines). All 24 post-Apply cube samples
per profile pass. texturearrays passes every value in Gamma, Linear and Android (36 imported
`Cubemap.GetPixelData` reads per profile resolved). textureimport stays exact (502 levels,
1,732,492 bytes per profile), spriteimport 519/520, resources 539/539 values and 62/62
images; assetclones keeps its eight main differences and resolves the four Alpha8 graphics
format values of each colour profile (109/109). No new Editor captures were needed.

## GPU buffer counters, copies, indirect dispatch and readbacks, 2026-11-01

NativeApi 15 with render protocol 7 (buffer copies, hidden counters, indirect
dispatches, texture level and layer readback); shader cache 16 and texture
cache 13 are unchanged. tests/projects/gpubuffers records Append, Consume and
Counter buffers, CopyCount, SetCounterValue, indirect dispatches, buffer copies,
command-buffer buffer data, SetData/GetData overloads and AsyncGPUReadback of
buffers and textures on OpenGL Core. Two windowed Native repeats are
byte-identical in their 389 values; readback completion frames differ between
repeats (one or two frames), so they are compared against the range the Editor
showed. Root's runtime passes 130 of the 442 compared keys and none of the 54
timings; the candidate passes 389/389 values and 53/53 timings. graphics 80/80,
materials 1004/1004, resources 539/539 values and 62/62 images, rtdepth
1071/1071 values and 316/368 images, controls 46/46 and 13/17, builtinfx
213/215 (2 expected) and 92/92 images, editor 170/174 (4 expected) and all 26
URP scenarios byte-identical to Root's runtime: none changed. Managed tests pass
1,289/1,300 with the same eleven failures; runtime 50, app 20 and shader 37
Rust tests pass; API checking has zero errors in 2,290 types with regenerated
stubs. Readbacks into unmeasured formats (16-bit normalized, compressed
sources) fail loudly; RequestIntoNativeSlice and the Awaitable overloads remain
stubs.

## Screen storage and ScreenCapture, 2026-11-01

The screen is stored as the Editor's game view stores it: 8-bit RGBA, drawn through
an sRGB view in Linear color space, and read back or copied as its bytes. It was a
half-float target: float reads returned linear values and byte reads were re-encoded
on the CPU, one step off at exact halves. The render texture probe now passes all
1,071 values and 368/368 images (316 before) and its controls 46/46 values and 16/17
images (13 before); the unsupported integer-array default binding remains. The camera
MSAA probe's Linear screen reads match as its Gamma ones do: 3,219/3,261 records and
405/433 images (3,201 and 381 before), none introduced. Graphics, resources,
built-in pipeline and effects, clipping, colors, focused camera, UI Toolkit and
materials keep byte-identical outputs.

`ScreenCapture` is implemented from tests/projects/screencapture, a new Linear and
Gamma reference of two byte-identical Editor captures each: the texture capture at
the call (RGBA32, linear, one mip, alpha kept, superSize re-rendering the cameras),
the render texture capture (one texel per pixel from the corner, decoded into linear
and float targets), and the file written after the end-of-frame callbacks as an RGB
PNG. Unbound passes Linear 174/187 values and 33/39 images, Gamma 163/174 and 33/35
(stubs: 88/187 and 0/39, 83/174 and 3/35). Retained: `LoadImage`'s ARGB32 format
names, reads in Update (the Editor's window framebuffer), and one-step superSize 3
differences at rounding boundaries. ReadPixels of the screen outside the frame logs
the Editor's error. In Gamma the Editor crashes on texture captures in Update or below
superSize one; the probe skips those there.

Managed tests pass 1,289/1,300 with the same eleven failures; runtime 50 and app 20
Rust tests pass; API checking has zero errors in 2,292 types and regeneration only
removes the ScreenCapture module's stubs. No ABI, protocol or cache version changed.

### 2026-11-01 combined async, culling and module validation

Frozen matching Root `5e396e08` runtime: 33 files; tested and packaged Core identical.
Managed 1352/1363, same eleven failure names/messages; project/runtime Rust 23/23 and 57/57.
API preflight reused: 0/2301 errors/types; only the ten Core RequestAsync stubs removed.
InstantiateAsync 731/777, retained 46 missing/46 extra; CullingGroup 1147/1149, retained 2/3.
Module compile 7/7, Play and Edit-to-Play 8/8; five assembly-identity/transition checks pass.
CLI fixture expectation corrected to measured ARGB32 and passed; no runtime-source changes.
GPU: Awaitable 225/225, buffers 389/389, review controls 15/15; all timing gates pass.
RT 1071/1071 and 368/368 images; twelve GPU jobs introduce no regressions; all 33 files unchanged.
Existing async/culling, screenshot and texture residuals remain; no full-parity claim or new Native capture.
Reports/manifests: `~/.cache/unbound/validation/runs/{integrated-root-validation-20261101-t3-6df242fc,matching-root-gpu-20261101-t3-c0b643d0}/`.

### 2026-11-01 combined callback and rendering validation

Source `4dd529c6`, frozen 33-file runtime; current-checkout Rust freshness and tested/package Core equality verified.
Early resources: 539/539 values, 62/62 images on both frozen before and candidate; omitted bundle fixture caused the same 26 missing plus one extra on both.
Managed 1380/1391, same eleven names/messages; affected Rust 146/146 and existing CLI fixture pass.
API 0 errors/2314 handwritten types, PhysicsModule consistent; inherited two-file generator differences byte-proved, no rewrites.
Callbacks retain 29 missing/29 extra, nine changed unmatched records and 25 ordering inversions; prefab fields retain 21/6280 differences.
InstantiateAsync retains 731/777, 46 missing/46 extra; CullingGroup 1149/1149 plus initial EndOfFrame extra; module compile 7/7, Play/transition 8/8 and five identity checks pass.
Matching GPU: 15 captures, zero introduced failures; CRT 349/352 + 100/140 images, flares 689/689 + 140/217; strict residuals retained.
One builtinfx residual improves pointwise; all unions/tolerances and 33 runtime files retained, with no new builds or Native captures.
Report/runtime/input/command manifests: `~/.cache/unbound/validation/runs/{combined-root-validation-20261101-t3-42333086,matching-closure-gpu-20261101-t3-de3d0dea}/`.

### 2026-11-01 audio adoption on combined callback and rendering source

Reviewed audio `0651b1d6` merged as `03e0be35` onto `ba3cfaba`, whose production sources equal `4dd529c6`.
Production delta: mixer lifecycle/routes and four AudioSource callback-discovery lines; stubs/API/ABI unchanged.
One release cycle; all eleven Rust workspace inputs and managed PDB sources verified; tested/package Core equal, 33 artifacts frozen.
Boundaries 5943389/5943389, lifecycle 94422/94422, generators 720907/720907 exact; historical unions resolve 5166198 and 104448.
All thirteen affected captures match reviewed PCM/metadata exactly; both-rate submission/Echo/filter and 48k startup/callback Native gates pass.
Introduced/retained zero; Rust audio 48/48, managed audio 19/19; Root RMS/wake/sink/thread guards preserved byte-for-byte.
44.1k dense startup/callback lack Native oracles; reset/snapshot 2061 per rate and historical matrix raw deltas remain unclaimed. No controlled 4dd audio A/B, graphics replay or new Native capture.
Full unions, runtime/input/compiler manifests and limits: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/audio-matching-validation-20261101-t3-174cb588/`.

### Curved trail birth samples and shallow joins, 2026-11-01

- Adopted d909 RNG history onto 875cfacd on the child branch; preserved Root audio/CRT/callback/flares sources and all generated stubs.
- Reused the existing Native pair only: both repeats agree on all 124 scoped public records; full reflected storage remains REJECTED with all 530 Vector4 deltas outside Git.
- Retained the first scheduled birth coordinate without an extra point; camera-plane sides and Native shallow miter blend close all 13 regressions. Trail raw RGBA equals Native; encoded PNG bytes differ.
- Focused managed 2/2 and RNG 27/27; all 65/40 uint seed controls exact, same 96 inherited full-value failures; streams 3544/3544 pass the original 2e-4 tolerance (3010 JSON-identical, 534 with tolerated differences), plus 3/3 image RGBA exact.
- One broad 1560-record/17-image run: numeric resolved1579/retained591/introduced0, original559 unchanged; randomized44 retained32/introduced0. Zero raw RGB-error worsenings across 17 images.
- Full raw image union retains 3271 differing pixels and diagnostic over8 retains1587/introduces0; 14/17 image RGBA exact. No image pass inferred from script exit0.
- All mesh fields pass original 2e-4 tolerance; 582 raw mesh bytes differ. Two RenderTexture flag fields remain, reproduced on frozen parent. Backing storage/GetTrails and capacity/rejected births remain unclaimed.
- Matching 33-artifact runtime, private build/OUT_DIR/PDB receipts, full unions and limits: `~/.cache/unbound/validation/runs/particle-trail-geometry-20261101-t3-4bbbf024/HANDOFF.md`.

### Particle package integration, 2026-11-01

Reviewed `4b0d7433` integrated on Root875; all 13 introduced trail pixel regressions close, with original 559 broad failures unchanged.
Rehashed all 33 runtime payload files against the external corrected manifest; production inputs equal the tested source, so affected gates are reused.
The copied embedded manifest is stale metadata and remains untouched. The helper baseline manifest names the wrong package; actual parent command/environment and pre/post hashes establish identity.
Independent migration audit uses 1122 changed OLD IDs, excluding the map header; no retired ancestors occur.
Full-public target.flags stays red (Native166/engine130), as on the frozen parent; opaque Trails storage remains unclaimed.
Final combined managed/API disposition is pending the platform integration; no duplicate GPU/seed/stream batch or new Native capture.
Review, complete recursive manifest, receipt corrections and gate limits: `~/.cache/unbound/validation/runs/particle-root-integration-20261101b/root-adjudication.json`.

### Dependency-fixed sphere trigger validation, 2026-11-01
Root29df + ordinary merge41b3981e + ordered-discriminant fix/test a71f6fba; reviewed RNG/trails and protected sources retained.
Literal Core-only attribution control has31 identical payload files and2 changed Core DLL/PDB files; no naturally built baseline claim.
565 strict union144 resolved/551 retained/48 INTRODUCED stays red; all48 trace unchanged input/before/IgnoreOutside bits, owned by Root input-construction/CoreCLR-math.
402 retains21 zero-life failures unchanged, introduces0; original55/150, focused9 and non-sphere96 pass. No prior28 substitution.
Under Root's explicit package-causality policy, affected3D103, queries895+20, geometry59/capsule554/sweeps5610 and loading4656 per order pass.
One matching-input broad1560/17-image capture resolves286/retains305/introduces0; all17 RGBA arrays equal Root baseline; original559 disposition286 resolved/273 unchanged.
One managed build cycle, no Rust/new Native; full managed/API remains separately pending. Full unions/manifests/limits: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/sphere-dependency-fixed-20261101-t3-a9beda03/`.
### 2026-11-01 application facts and platform constants

Managed-only on accepted Root `a90ca416` runtime (CLI/native unchanged, own Core/Host): `isLoadingLevel`, `buildGUID`, `consoleLogPath`, `OpenURL`, `HasProLicense`, `ExtractStackTraceNoAlloc`, startup logs, Profiler memory sizes, SystemInfo CPU/OS facts.
Appfacts 497/555 (before 38/555): 461 resolved, 84 retained (18 missing, 26 extra, 40 different), none introduced; tolerances unchanged.
Platform, lifecycle exact; messages 316/316. Managed 1380/1391, same eleven names/messages.
API 0 errors/2314 types; generated stubs byte-identical to the accepted generator output, no stub regeneration.
Open: unload timing, stack frames (resolved project path, inlined dispatch), StackTraceUtility stub, audio/component/material sizes, compressed texture creation; GPU identity is not read (marshalling disabled in CoreModule).
Report: `~/.cache/unbound/validation/runs/claude-fresh-20261101b/platformconsts/`.

### 2026-11-01 platform facts integration on Root875, matching correction pending

Ordinary merge of platform package `793acef7` onto `875cfacd`; accepted Rust/native/audio, CRT/callback/module behavior retained.
Added pre-mutation log containment and rotation checks, stdout mode, real-time URL recording completion, and checked EGL cleanup without shared-display termination.
Latest compiled source `022ee063`: TestCore equals packaged Core; 25/40 affected tests pass, including all seven injected EGL cleanup controls.
Fifteen log tests failed at fixture setup because the existing validation-cache relocation was denied; corrected source adds that existing permitted cache root and an authored-target veto, not yet rebuilt.
Comparator controls 9/9 and recording protocol controls 4/4; historical appfacts loaded 493/555 and solo 497/555 remain intact, including all 84 residuals and raw changes.
Three pure-managed attempts retained, including the prior commit-metadata Core mismatch; no current-source runtime or affected-probe acceptance claimed. Additional correction-cycle authorization pending.
No new Native/Cargo, ABI/version/stub changes, broad suites or reference edits. Newer Root particle integration and final managed/API combination remain Root-owned.
Full attempts, source correction, historical capture hashes and prepared matching commands: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-root875-20261101-t3-394c2f9d/PENDING_HANDOFF.md`.

### 2026-11-01 bounded current-Root platform correction, acceptance blocked
- Ordinary merge of prepared ae110878 onto Root870 preserves RNG/trail/sphere/audio; log opening now snapshots reachable authored aliases once for both destinations before mutation.
- Cheap exact-source log controls30/30 and packaged-Core original red witnesses4/4 preserve temporary authored sentinels; new alias controls13/13 pass in the sole full managed TRX.
- One frozen f26e8eb9 Host/Core/test cycle: managed1464/1475, identical11 inherited names/messages; focused RNG27/trail2/sphere9/platform55 pass. TestCore equals packaged Core; all33 artifacts and PE/PDB/input audits pass.
- Normal/delayed Appfacts497/555 each, retains84/introduces0; original loaded493/solo497 retained; platform/lifecycle exact and messages316/316. Both actual machine checks FAIL physical console-log spelling; rooted fresh nonempty files proved separately.
- Exact prior-platform API reuse BLOCKED by inherited axis-allocation annotation removal and generated-stub deltas. Full/API recording overlap NOT established; initial API assertion prevented tests and actual API work started after recording.
- Unchanged runner rewrites copied ProjectVersion.txt; strict fixture-equality audit fails, with complete differences retained. No correction/rebuild/recapture follows these failures; one corrective budget consumed, runtime REJECTED.
- Full source, commands, TRX, metadata, manifests, 555-key raw unions and failures: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-corrected-root-20261101-t3-2f1786c1/HANDOFF.md`. Root48 strict introductions OPEN,21 zero-life unchanged,305 broad Native failures; no full Unity parity claim.

### 2026-11-01 platform checker reevaluation, proof acceptance blocked
- Checker accepts an absolute physical alias only when it strictly resolves to the recorded project's expected fresh, nonempty log; controls18/18, old checker17/18, protocol4/4.
- Existing normal/delayed machine commands now pass with recorded checker hashes; raw captures and old failed reports remain unchanged. Exact Native comparisons remain497/555 with the same84 residuals and complete original unions.
- Compiled ApiProjection on Root870 Core276a28c5 and candidate Corec2608dc1 retains65,228 rows each: four prepared-platform provenance annotations differ. Exact metadata equality is false; no projected Unity declaration delta or attribute filtering. Old-platform RNG annotation removal remains explicit.
- Generated files equal Root870 exactly; separately retain the old generation's three additions/two changes. Historical API0/2314 is preserved, not claimed as a fully reused current gate.
- Archived legacy runner hash and identical copy_project normalization verified; Native launch-to-runner/pre-launch-version binding is missing. Both file hashes and prior failed fixture audit remain; fixture acceptance BLOCKED.
- No build, Native/engine capture or full-suite rerun. Reuse matching TestCore full1464/1475, identical11 failures; frozen33 verified before/after. Conditional load-overlap capture not started; independent source-safety verdict remains required. Root48 OPEN,21 zero-life/305 broad failures retained.
- Commands, source hashes, complete metadata/stub/input/manifests and blockers: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-proof-checker-20261101-t3-ffc634df/HANDOFF.md`.

### 2026-11-01 launch-bound platform corroboration, canonical disagreement
- Added optional admitted prelaunch byte/hash receipts to the shared Editor runner; fake-Editor controls19/19. Production f26e8eb9, Generated and canonical files unchanged.
- Root authorized one unchanged windowed GPU pair on6000.0.71f1 with private displays and hard1200/progress480 deadlines. Both exit0 on NVIDIA; OS/machine checks pass and complete555-field repeats are byte-identical.
- Each matches551/555 canonical fields. Exact retained differences: stack.noalloc.4096.{empty,assets}.{return,written} are646 instead of577. No normalization, reference publishing/replacement or second pair.
- Prelaunch runner/CS/assets/version/profile/spec/env receipts are bound inside each admitted lease; both copied fixtures equal the prepared engine inputs before Editor mutation. Post-mutation version bytes remain separate.
- One isolated API-only no-stubs/entrypoint compilation passed; current-source/PDB/PE/revision/reference inputs verified. Proper source-aware API execution and delayed engine capture remain UNCONSUMED because Native corroboration failed; no runtime/Host build or managed rerun.
- Matching full1464/1475 and exact11 failures, RNG27/trail2/sphere9/platform55 and existing497/555 exact84 unions reused. Frozen33 and old446 evidence files verified unchanged; source-safety remains separately owned. Root48 OPEN,21 zero-life/305 broad failures retained.
- Full commands, launch bindings, all555 raw values/deltas, annotation diagnostics and limits: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-acceptance-20261101-t3-eaec5c99/FINAL-HANDOFF.md`. PLATFORM remains REJECTED; no consumable accepted runtime.

### 2026-11-01 platform validation stopped on independent source review
- Root rejects frozenf26e8eb9 after independent P2 findings: Resolve bounds recursion depth rather than total symlink follows; textual link/.. collapse can miss authored membership and clobber a sentinel. No review experiments or fixes repeated here.
- The already-completed sole Native pair remains551/555 each with four identical canonical disagreements. No replacement pair/reference edit. Prepared API Check and delayed engine/overlap remain unexecuted; no runtime/Host rebuild or fullsuite repeat.
- Existing same-file machine-check passes establish identity/freshness only. Both raw logical/physical consoleLogPath spelling disagreements remain game-visible and unmeasured; no string-equality claim.
- Exact budgets, review hash, raw spellings, completed launch receipts and stop disposition: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-acceptance-20261101-t3-eaec5c99/ROOT-STOP-RECEIPT.md`. No consumable accepted runtime.


### 2026-11-01: bounded platform resolver correction, focused gates only

Merged delivery4641d721 from Root870; applied reviewed patch01a926f5 plus exact40/41-link controls in ff28ada4/227643f9. One private coherent Host/Core/test cycle;64/64 focused, red4 preserved, checker18/protocol4 pass. Real current-source no-stubs API Check0 errors/2314; all33/PDB/source bindings and TestCore equality retained, CLI1b242614/PhysX487d450b reused. One delayed engine exits0, strict machine passes,497/555 with exact84 retained keys and no introduced/resolved keys. Nine raw stack4096 changes versus prior delayed capture, including four numerical worsenings, remain explicit; actual Check CPU overlaps opener delays. Prior1464/1475 exact11 suite is prior-source evidence only. Native pair remains551 corroborated plus four unresolved stack-fixture disagreements. No oracle/stub/ABI/cache change; Root combined disposition pending;48 sphere input bits OPEN,21 zero-life unchanged,305 broad failures. Full receipt: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/platform-acceptance-fixes-20261101-t3-e6d30c4f/HANDOFF.md`.

### 2026-11-01 Root platform integration, partial
- What: measured application/SystemInfo/profiler facts, EGL identity and guarded session logs; source equals compiled227643f9.
- Probe:497/555 reference fields;84 union failures retained,18 missing/26 extra/40 different.
- Introduced:0 normalized leaf failures vs prior f26 and historical Root;9 stack values changed,4 numerical errors worsened and remain open.
- Managed:64/64 current focused;1464/1475 with11 inherited failures is prior-source evidence only, not a current full-suite result.
- API:real Check0/2314; checker18/18,protocol4/4,strict machine and actual Check CPU/load overlap pass;33 payloads verified.
- Native:independent repeats agree,551/555 old-canonical fields;4 stack-fixture disagreements remain, no reference replacement.
- Open:84 platform failures, filesystem/macOS/TOCTOU limits and current combined full suite; line package remains rejected. Receipt: `~/.cache/unbound/validation/runs/platform-root-integration-20261101b/root-adjudication.json`.

### 2026-11-01 SpriteMask opaque-stage dispatch, mask-only runtime
- What: a tagged built-in forward opaque emission no longer adds new later-queue mask stencil writes; existing tracked masks and Inside/Outside ref-1 tests stay. Root review fixes are included.
- Lines: canonical 1853, union 1870, failing keys 1679->1678 (inside mask resolved); introduced keys/leaves/pixels/channels 0. Outside keeps 590 failing pixels and 246 raw channel-error increases.
- Resources 539/539 values, 62/62 images, byte-identical. builtinrp/lighting/graphics/urp byte-identical; builtinfx differs only in an expected flare image.
- Managed 1488/1499, exact known 11; mask 15/15, platform 64/64. API 0 errors/2314; stubs [] (historical generator differences reported, not written).
- Open: masked VertexLit/deferred/renderer-list/override/callback/depth-clear runtime paths are unmeasured; line geometry, LightProbes, sphere48 and zero-life21 remain with their owners.
- Receipt: `~/.cache/unbound/validation/runs/mask-only-integration-20261101-t3-65ed30f3/HANDOFF.md`.

### 2026-11-01 Root SpriteMask dispatch integration
- What: only actual built-in forward-opaque draws defer NEW later-queue masks; existing stencil contributions and other dispatch paths retain their handling.
- Probe: inside-mask144->0 failing pixels; outside590->590; other7lineimages byte-identical, resources539+62 exact.
- Introduced:0 numeric/pixel/channel failures; outside246rawchannel errors increase and330decrease, all retained in full reports.
- Managed:current1488/1499, same11 failures; all15mask and64platform controls pass, testedCore=package.
- API:0/2314 through existing legacy-discovery entry; no public/API/stub/ABI/cache change;33payloads Root-verified.
- Controls:builtinrp/lighting/graphics/urp unchanged; one flare image change remains unattributed, not a mask claim.
- Open:maskedCB/SRP/deferred/retained-target runtime coverage, line/LP packages and original gaps. Receipt: `~/.cache/unbound/validation/runs/mask-root-integration-20261101b/root-adjudication.json`.

### Combined line/trail queue validation, 2026-11-01
- Integrated authoritative5a plus Root COW append and isolated queue tests; production confined to LineRenderers/PlayerLoopDriver.
- One coherent Host/Core/test cycle, all33/PDB inputs bound, TestCore equals packaged Core; full managed1451/1462, exact known11 unchanged, line/queue33/33, Python8/8.
- API/no-stubs0 errors/2314 types; source stub changes[]. Resources539 values+62 images pass with mip-bundles.
- Required line1870 union:1654 pass/215 fail/1 resolved-extra; full Root/blend/final1887 keeps17 baseline-only keys. Both EOF introductions and state resolve;15 timing rows remain.
- REJECTED:60 newly failing numeric/index paths and95 pixels versus matching Root, inside retained failing keys;36 numeric paths versus blend. All raw/error changes retained, no masks/tolerance changes.
- Auto3557 BLOCKED at shader setup in Root/final; one runner correction failed, attempts preserved. No extra build/capture/Native; sphere48 and zero-life21 remain OPEN.
- Report: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-combined-20261101-t3-9d5972f4/REPORT.md`.

### Offline line geometry attribution, 2026-11-01
- Source `186e411d` on authorized ba658956; world TransformZ render plane prepared separately from measured BakeMesh, UNBUILT/UNACCEPTED.
- All60 Root index introductions are30 one-cap TransformZ trail meshes; all36 blend introductions are round-trail tangent components. Full171-cap/37-lighting controls retained.
- All95 pixel/388-channel introductions predate blend; exact witnesses and CPU replays retained. Native trail replay still differs at88 pixels.
- Offline only: tilted image212->0 failing pixels, complete image864->652; Root introductions95->80 pixels/388->328 channels. Numeric60/36, retained4968/136, increases1279/480 unchanged.
- Measured package remains REJECTED with all60/95 introductions and15 time failures; no new build/capture, managed test UNRUN, API/stub changes[]. Mask/cap/tangent blockers and sphere48/zero-life21 remain.
- Root combines platformd662 and separate runnerf60 later; no runner import here. CCC timeout124 closed and registration removed; no owned clients remain.
- Report: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-geometry-attribution-20261101-t3-dd198b0b/REPORT.md`.

### Offline corner/per-segment line geometry, 2026-11-01
- Source 5d86c5a2 on prepared 2c604df5: per-segment width/color by index, un-eased corner arc side/turn plus normalized position angle, TransformZ trail corner side swap. UNBUILT/UNACCEPTED.
- Float32 replay reproduces all saved engine meshes before the change; prediction: affected components 5746->44, none introduced; tangents 368->0; whole failing keys 215->83.
- All 8 original trail channels and alpha 2055 predict within 2/255; nine-image failures 864->536 pixels, 2630->1880 channels; Root introductions 80 mask pixels only.
- Numeric vs Root/blend/final: introduced 0/0/0; 32 above-tolerance increases vs final, all inside already failing components (max 6.7e-5).
- Open: trail 88 pixels = tex.Static render U offset (scale x travelled path, one witness); 8 zero-area join indices; mask; 15 time rows; sphere48/zero-life21.
- Cap-two inward plane remains inference (hidden non-emitting points zero the width). Cap-count layout proposal prepared, not published; append stays refused.
- Four managed tests UNBUILT/UNRUN; API/stub/ABI []. Report: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-corner-alpha-20261101-t3-dae2fa72/REPORT.md`.

### Combined line runtime on the mask Root, 2026-11-01
- Merged prepared line geometry (8dff) plus runner f60 onto mask Root 48db; published the Root-approved 16-row cap-count control file (main reference and images unchanged, append still refused).
- One build plus one test correction (positionCount); 33 payloads bound, TestCore equals Core; managed 1534/1546 = known 11 plus one stale assertion that contradicts Native.
- API (current platform-module discovery) 0/2314; stubs [], no public API delta.
- Lines on matching Root cf0: 1702 -> 81 failing, 0 introduced keys/paths/pixels/channels; 8 of 9 images exact; trail image 88 (Static U); 15 time rows unchanged.
- Autodestruct fully exercised: 1918 -> 3142 of 3557 pass, but 382 rows introduced (trail count +1 at early steps; passive step-0 witness). Resources 539+62 exact.
- NOT accepted: one stale test and the 382 autodestruct rows stay open; no third cycle.
- Report: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/combined-line-runtime-20261101-t3-ef952536/REPORT.md`.

### Trail first-move recording on Root cf0, 2026-11-01
- Validated source 6ef53d16 (private trail state bit plus Root test fixes); one build/test cycle, 33 payloads bound, TestCore equals Core, CLI/PhysX from cf0.
- Managed 1537/1548 = exactly the known 11; line tests 28/28. API: reused 0/2314 report, public metadata equal (65228 rows), stubs [].
- Lines: 1702 -> 81 failing, 0 introduced keys/paths/pixels/channels vs Root, blend and the earlier combined run (raw values identical to it).
- Autodestruct: 3545/6/6/6 of 3557, all 382 earlier introductions closed, 0 introduced; 18 manual-destroy delivery rows retained (fail on Root too).
- Resources 539+62 byte-identical. Open: Static U 88 px, 15 time rows, 8 zero-area indices, cap-two plane inference.
- Report: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/trail-recording-runtime-20261101-t3-fcf65aff/REPORT.md`.

### Root line integration, 2026-11-01
- What: admitted the measured line/trail package; production sources equal tested 6ef53d16, with only later documentation changes.
- Probe: lines retain 81 failing keys in the full 1903-key union; autodestruct 3545/3557; resources 539 values + 62 images pass.
- Introduced: 0 failing keys/numeric paths/pixels/channels; all 425 raw numeric error increases retained, including 205 above tolerance inside already failing paths.
- Managed: 1537/1548, identical 11 inherited failures; line 28, queue 25, mask 15 and platform 64 pass. Root rehashed all 33 payloads; TestCore equals package.
- API: reused proper nine-module 0/2314 result with identical public metadata; stub/API/ABI/cache changes [].
- Open: 18 manual-Destroy timing rows, 15 line time rows, 88 Static-U pixels, 8 zero-area indices and cap-two inference; LightProbes remains held.
- Evidence: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/trail-recording-runtime-20261101-t3-fcf65aff/REPORT.md`; runtime `candidate/`, Core 5c7b03ce; Root receipt `line-root-integration-20261101b/root-adjudication.json`.

### Root light-probe and scene-creation integration, 2026-11-02
- What: admitted the measured LP, indirect-specular and Single-load creation-ownership scope; production equals tested d7ff35ca, including the reviewed three-line test compilation fix.
- Probe: LP 367/625 and PNG 57600/57600 exact; lifecycle 10/31; creation contract 3/3, strict canonical 30/84. Other affected scene captures retain their earlier results.
- Introduced: creation raw union preserves 82 newly differing leaves from existing listing/root-query gaps and scene-label shifts; semantic ownership/lifetime/Find groups introduce 0, resolve 128 and retain 94. No precision waiver covers a functional gap.
- Managed: 1571/1582 with identical 11 inherited failures; all seven new/changed scene tests pass. One compile-error correction used; no extra Native or Rust build.
- API: reused proper nine-module 0/2315 report with public metadata equality; generated removals are LightProbesQuery plus eight MPB copy methods. ABI unchanged; baked-lighting importer local version 5->6.
- Runtime: Root rehashed all 33 files and 1089 PDB-source documents, verified TestCore equals package Core 5c2bf5cc, and checked 265 unchanged CLI inputs plus the separate managed build trigger. Frozen prior runtimes are unchanged.
- Open: hull/query indices, unload order/state, pending listing/root queries, LP reload identity and empty-active-scene probes; precision ledger values stay raw. All 56 earlier pixel introductions are fixed, not waived.
- Evidence: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/claude-fresh-20261101b/creation-validation/RECEIPT.md`; Root receipt `validation/runs/lp-ownership-root-integration-20261102/root-validation.json`.

### Root three-API integration, 2026-11-02
- What: admitted measured Pose, LazyLoadReference/Object helpers and batch GameObject scope; production equals tested 0d49b10d, including the clone-test cleanup and explicit unresolved-Lazy YAML-save guard.
- Probe: coreapis 289 matched / 393 missing / 523 extra of 682 reference lines, resolving 280; held OnDemand remains open. Lifecycle/graphics/assetclones byte-identical; messages 316/316 with unchanged timing invariants.
- Introduced: 0 previously matching reference lines lost; 80 newly unmatched actual lines and 114 removed extras remain in the full union. Probe defects and significant API/text/order gaps are explicit, not precision waivers.
- Managed: 1576/1587 with the identical 11 inherited failures; all five new tests pass. One normal build and one suite; corrective cycle unused, no Native or Rust builds.
- API: fresh nine-module check 0/2317, all 108 generated files equal; 41 generated lines removed for the three implemented areas. OnDemand stubs remain; ABI unchanged.
- Runtime: all 33 payloads and 1093 PDB-source documents rehashed; TestCore equals package Core abe89e5b. Four activation traces agree after metadata-only normalization; Native plain SetActive remains unmeasured.
- Open: 11 significant child-first batch-order pairs, instance-id signs, JSON/format/exception text, lossless unresolved-Lazy YAML saves and per-copy scene resolution. Twenty-one Pose rotation keys are ledgered by absolute error, maximum 2.384185791015625e-7.
- Evidence: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/claude-fresh-20261101b/core6-validation/RECEIPT.md`; Root receipt `validation/runs/core6-root-integration-20261102/root-validation.json`.

### Root OnDemand integration, 2026-11-02
- What: admitted measured built-in OnDemandRendering, offscreen preparation and coroutine EOF gating; production equals tested d0378bce. Awaitable, Time counter placement and handler dispatch are unchanged.
- Probe: coreapis 356 matched / 326 missing / 326 extra of 682 reference lines; screen/GUI/EOF schedule matches all 17 window frames and RT callbacks run each frame. Screencapture Linear/graphics/lifecycle identical; messages 316/316.
- Introduced: 0 lost reference matches, 67 resolved and 262 newly unmatched actual lines, all preserved. Their counter phase and OnRenderObject order also occur on the frozen baseline; no diagnostic normalization enters the comparator.
- Managed: 1580/1591 with the identical 11 inherited failures; four new tests pass, including isolated mask/raster preparation. One build/suite, corrective unused, no Native or Rust builds.
- API: fresh nine-module check 0/2318; all 108 generated files equal. Only the ten-line OnDemand stub block is removed; ABI unchanged.
- Runtime: all 33 payloads and 1095 PDB-source documents rehashed; TestCore equals package Core cbf30c19; 265 compiled CLI inputs unchanged. Executed before-inputs match; the coreapis README-only change is explicit.
- Open: significant renderedFrameCount phase and OnRenderObject handler order; no-RT visibility, SRP/custom dispatch, motion, panels, skipped readback and async EOF remain unmeasured. Earlier Core/LP/line gaps stay open.
- Evidence: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/claude-fresh-20261101b/ondemand-validation/RECEIPT.md`; Root receipt `validation/runs/ondemand-root-integration-20261102/root-validation.json`.

### Root rendered-frame counter integration, 2026-11-02
- What: admitted the measured counter-phase correction; production equals tested 60a1cd68. The counter advances at GUIClearEvents after coroutine EOF, once per rendered or skipped frame; handler dispatch is unchanged.
- Probe: coreapis 594 matched / 88 missing / 88 extra of 682; all 313 window counter reads match. Messages 316/316, lifecycle and all 50 screencapture files retain their prior results.
- Introduced: 0 lost reference matches, 238 resolved, 24 newly unmatched actual ORO lines and 262 removed extras, all preserved. The 64 non-window pairs and ORO order are unchanged.
- Managed: 1583/1594 with identical 11 inherited failures; three new tests pass. One build/suite and four engine runs; corrective cycle unused, no Native or Rust build.
- API: reused nine-module 0/2318 result through 65228 equal public/protected metadata lines and unchanged generated files/tools/projects; no API, stub or ABI change.
- Runtime: Root rehashed all 33 payloads, verified TestCore equals package Core eaa489ea, PDB source checksums and 265 unchanged compiled CLI inputs; the old runtime stays frozen.
- Open: significant ORO order, exact Unity counter node, custom/paused/Edit loops, intervening readers and startup boundaries; earlier Core/LP/line gaps remain. No comparator normalization or tolerance change.
- Evidence: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/claude-fresh-20261101b/rfc-validation/RECEIPT.md`; Root receipt `validation/runs/rfc-root-integration-20261102/root-validation.json`.
