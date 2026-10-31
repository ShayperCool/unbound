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
Targeted combined capture replay is running. Earlier model, camera and material
results remain in the immutable panel-models-native9 artifact set. The text probe
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
