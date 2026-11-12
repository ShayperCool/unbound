# Light probe API probe

Built-in render pipeline, linear color space. Two scenes baked by the Editor itself in Shadowmask
mode (CPU lightmapper, no denoising, filtering or compression), so their light probes carry
probe occlusion: `Main` (a static ground and wall, a mixed directional and a mixed point light,
18 probes on a jittered 3x2x3 grid, no five of them on a sphere so the tetrahedralization is
unique) and `Extra` (8 jittered probes beside Main's and a mixed point light), loaded
additively at run time. `Assets/Editor/LightProbesSetup.Create` made and baked both
(`run.py create`); the scenes, bakes and metas in `Assets/Scenes` are the Editor's own.

`Assets/Scripts/LightProbesProbe` records, as JSON lines (key, value; floats round-trip):

* the probes: counts (`count`, `countSelf`, `cellCount`, `cellCountSelf`), positions, every
  baked probe's coefficients, `GetSharedLightProbesForScene` per scene, the lights' baking output;
* at ten points (inside the hull, at a probe, outside it, far away, inside Extra's hull):
  `GetInterpolatedProbe` (with and without a renderer hint), `CalculateInterpolatedLightAndOcclusionProbes`
  (SH and occlusion), a `LightProbesQuery`'s probe, occlusion and tetrahedron index, and the
  occlusion at every probe position;
* the List overload (what the result lists hold), every argument error (type and message);
* `LightProbesQuery`: index hints chained from point to point and out of range (-1, the cell
  count, int.MaxValue), the batch overload with mixed hints, short and longer outputs, a query
  created before `bakedProbes` changes (it keeps the old probes), Dispose, double Dispose, use
  after Dispose, a default query and Dispose(JobHandle);
* what `MaterialPropertyBlock.CopySHCoefficientArraysFrom` and `CopyProbeOcclusionArrayFrom`
  store (the arrays `unity_SHAr` .. `unity_SHC`, `unity_ProbesOcclusion`): whole arrays and
  lists, ranges into a new block and into existing arrays, existing arrays of another length,
  every argument error;
* a 320x180 image (`probe.png`) of three rows of spheres on their own layer with the lights off:
  `Graphics.DrawMeshInstanced` with five synthetic probes from such a block
  (`LightProbeUsage.CustomProvided`), `DrawMeshInstanced` with `BlendProbes`, and MeshRenderers
  whose block holds one synthetic probe (`CustomProvided`); five pixels of every sphere as JSON;
* the events (`lightProbesUpdated`, `needsRetetrahedralization`, `tetrahedralizationCompleted`)
  around `Tetrahedralize` on Main alone, an additive load of Extra, `Tetrahedralize` with both,
  the unload of Extra and `Tetrahedralize` again, and the probes and interpolation at each step.

Not recorded: ranges written past the end of a block's arrays (the Editor writes out of bounds:
two repeats read different memory) and `TetrahedralizeAsync`'s completion (one repeat completed
within 30 frames, the other not before the scene unload; in both the probes changed only at
completion and the call raised no event).

    python3 tests/projects/lightprobes/run.py create --release 6000.0.71f1 --label <L>    # after changing LightProbesSetup
    python3 tests/projects/lightprobes/run.py editor --release 6000.0.71f1 --label <L>    # two Editor runs
    python3 tests/projects/lightprobes/run.py publish --release 6000.0.71f1 --label <L>   # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> UNBOUND_MANAGED_DIR=<managed> python3 tests/projects/lightprobes/run.py unbound --release 6000.0.71f1 --label <L>
    python3 tests/projects/lightprobes/compare.py tests/projects/lightprobes/Reference/unity-6000.0.71f1/probe.jsonl \
        ~/.cache/unbound/validation/runs/lightprobes/<L>/unbound/probe.jsonl

## What the Editor does (6000.0.71f1)

Two independent captures agree byte for byte (Renderer: NVIDIA GeForce RTX 4060).

* The original outside points are outside the probe positions' axis-aligned bounds. Queries
  there, with or without a renderer, give the ambient probe, occlusion (1, 1, 1, 1) and index -1.
  This does not establish an ambient fallback everywhere outside the convex hull. The geometry
  controls below record an open hull cell supplying lighting inside those bounds.
* A probe's occlusion is a vector by shadow mask channel: the occlusion of the mixed light baked
  into each channel. Only channels present at a vertex contribute its weighted value. A channel
  absent from every vertex of the selected cell returns 1. Channel presence includes vertices
  with zero weight, so a boundary cell choice can change occlusion even when SH agrees.
* `CopySHCoefficientArraysFrom` stores ShadeSH9's seven constants per probe; a block without the
  arrays gets arrays of `count` elements, existing arrays keep their length. Instanced draws take
  element i for instance i; a renderer's block of one element lights the renderer.
* `LightProbesQuery` keeps the probes it was created with; hints out of range start the walk
  anew; the batch overload writes the found cells back into the index array.
* Loading Extra raises `needsRetetrahedralization`; the probes (count 18) do not change until
  `Tetrahedralize` (26 probes, 121 cells, `countSelf` 18); unloading it raises the event again
  and keeps the 26 probes until the next `Tetrahedralize`. A scene load raises no
  `lightProbesUpdated`.

### Scene unload and Single replacement (`lifecycle.jsonl`)

`Lifecycle/LightProbesLifecycle.cs` (`run.py lifecycle`, `run.py lifecycle-publish`) records by
sequence index every probe and scene event of an additive `UnloadSceneAsync` of Extra and a
Single `LoadScene` of Extra, with the global counts and each scene's validity and
`GetSharedLightProbesForScene`. The Reference was published from two byte-identical captures.

* Additive unload: `UnloadSceneAsync` returns first (the scene is valid, not loaded, and its
  shared probes are its own). Then come `needsRetetrahedralization`, `sceneUnloaded` and the
  operation's `completed`, with no probe change in between. `completed` already sees an invalid
  scene whose shared probes are null. The global keeps the combined 26 probes / 121 cells until an
  explicit `Tetrahedralize`.
* Single load: no `needsRetetrahedralization` at all. At the outgoing `sceneUnloaded` the global
  probes (the outgoing scene's own) have `count` and `cellCount` 0 but keep `countSelf` and
  `cellCountSelf`. The incoming scene's probes are global by `activeSceneChanged`, with no event.
* A scene created by a load but not loaded yet, and an invalid scene, have null shared probes.

An engine capture of the witness (`run.py lifecycle-unbound`, UNBOUND_CLI / UNBOUND_MANAGED_DIR
as above) is compared with `compare_lifecycle.py REFERENCE CAPTURE --report R`: row by row and
strictly (every field of every row; missing/extra rows fail), with an informational alignment of
the event sequences so a moved event shows as one insertion and one deletion.

Unbound follows the relative order and the public state of both paths
(`LightProbes.SceneUnloaded`, `LightProbes.SingleLoadReplacing`, `SceneManagerInternal.UnloadForSingleLoad`;
unit tests in `managed/UnityEngine.Tests/LightProbesTests.cs`). It has not been run against this
Reference. Still open:

* Unbound unloads within `UnloadSceneAsync`. The scene is already invalid when the call returns,
  and the needs / `sceneUnloaded` group fires inside the call, so the lifecycle rows of that
  window do not match yet.
* Unmeasured: a Single load replacing several scenes (Unbound resets once for all of them), the
  Editor's `OpenScene`, Play mode entry and AssetBundle scenes (they keep the per-scene unload),
  `LoadSceneAsync` Single (it shares `LoadScene`'s path in Unbound), and `TetrahedralizeAsync`'s
  completion frame. The witness records no frame numbers.

### Objects made while a Single load unloads the outgoing scene (`creation-ownership.jsonl`)

`CreationOwnership/` (`run.py creation-ownership`, `run.py creation-ownership-publish`; suite and
points declared in `schema.json`) records which scene owns objects made during a SceneManager
Single load's unload, and what becomes of them: (i) a `new GameObject`, (ii) an `Instantiate` of a
tiny Resources prefab, (iii) a child of (ii) moved to the root with `SetParent(null)`, all inside
the first `sceneUnloaded` handler, and (iv) a root made in `OnDestroy` of an outgoing-scene object.
A second Single load's handler calls `CreateScene("Made")` and `SetActiveScene`; a third Single load
follows it. Each point records existence, owning scene, parent/root, scene-root, `Find` and
`FindObjectsByType` membership, the message order and the global light probe counts.
The two Editor repeats run one after the other; the second starts only if the first passes
every gate. `CreationOwnership/offline_compile.py` compiles the witness with an Editor-compiled
fixture copy's own compiler arguments, and `CreationOwnership/guards.py` checks the gate against
damaged synthetic captures.

The first pair (schema 1; both repeats exit 0, gated) was not published: it differed in 11
order-only leaves, namely which outgoing roots were already destroyed when the destroyer's
`OnDestroy` ran, the name order in Unity's "Some objects were not cleaned up when closing the scene"
error, and the destruction order of the four leftover objects at the next Single load. Every
existence, scene, parent/root, `Find`/`FindObjectsByType` and light probe value agreed.

Schema 2 predeclares a contract for exactly those three families (`schema.json` "contract",
implemented only by `CreationOwnership/contract.py`). A `CreationTeardownWitness` on every authored
Main root writes its `OnDestroy` to `creation-ownership-teardown.jsonl`. The outgoing roots at
`t1.a.iv` must then equal the setup roots minus those already destroyed in that repeat's own
trace. The error's names are an unordered set, with the text otherwise exact. At t2 every made
object still gets `OnDisable` before its own `OnDestroy`, all before its scene's `sceneUnloaded`, but
the objects may come in any order. Each repeat must satisfy these predicates (part of its gate).
The pair publishes the canonical form, `Reference/unity-<release>/creation-ownership.jsonl`, only if
the two canonical forms are byte-identical; everything outside the families stays strict.
`creation-ownership-publish` writes `publication.json` with every raw difference and its family.

The schema-2 pair (two sequential repeats, each exit 0 on the NVIDIA renderer and gated, the first
before the second started) satisfied the contract in both repeats. Its 12 raw leaf differences
were all inside the declared families: 5 or 4 outgoing roots destroyed before the destroyer, the
error's name order, and the t2 cleanup order. The canonical forms were byte-identical, and that
form is the 6000.0.71f1 `creation-ownership.jsonl` Reference.

An engine capture (`run.py creation-ownership-unbound`, UNBOUND_CLI / UNBOUND_MANAGED_DIR as above)
writes the same three outputs. `compare_creation.py REFERENCE CAPTURE_DIR [--native DIR ...]
--report R` reports each section independently: the witness gate; each family's predicates
against the capture's own trace; the canonical comparison with the Reference (only when every
predicate holds; the contract is never relaxed); the raw positional and event-aligned leaf
comparison against the Reference body, each leaf labelled with the family that could explain it
or none (strict); per declared point and alias, ownership, lifetime and Find fields as separate
groups, with the scene listing, light probes, lifecycle sequences and error name sets; and, with
`--native`, the full raw union against each raw Native repeat. Exit 0 only for a canonical match.

### Measured scope admitted on 2026-11-02

Validated source `d7ff35ca` established the admitted light-probe API, baked indirect
specular fallback and Single-load creation ownership. Source `ebb1ee50` additionally
corrects public scene listing, loaded state, root queries and paths. Source `9bce9c82`
keeps the global probe set at the recorded outgoing OnDestroy snapshot, with the Single-load
reset after outgoing object destruction and before sceneUnloaded. This is partial Unity
parity. The comparators retain their strict failures and original tolerances.

At that admission the main probe matched 367/625 values and its image matches all 57600 pixels exactly.
The lifecycle comparison now matches 14/31 rows. Creation ownership satisfies all three
predeclared contract rules; its full canonical comparison matches 65/84 rows. The
unrelaxed main comparison against each raw Native repeat matches 57/83. The
four created objects belong to the incoming scene from Awake, survive its load,
are discoverable during teardown and are destroyed when that scene next unloads.
Explicit parents and loaded requested scenes remain authoritative.

The combined scene-query matrix keeps all 1752 observations: 167 listing/loaded/root
targets and 79 path values now match Native, with zero lost matches. The remaining 44
differences are 20 light-probe values, 22 async-order values and two destruction-prefix
observations assessed by the creation contract. Their raw answers are unchanged.
Closing scenes report unloaded during destruction; queued incoming and closed outgoing
scenes are listed during teardown. A closed scene returns no roots and remains publicly
valid until teardown ends. Internal loaded membership and Forget timing are unchanged.
Invalid scene paths are null; the persistent scene path is `DontDestroyOnLoad`.
Reports are in `validation/runs/query-corrections-validation-20261102/` under the
validation cache. The 625-row main capture and its PNG are byte-identical to the prior
accepted runtime.

Significant gaps remain open:

* The combined hull has 120 cells versus 121; query indices differ. No hull-fill
  patch or weight normalization was admitted.
* Unload callbacks still run inside `UnloadSceneAsync`. The dying object remains
  active and findable.
* Activating a handler-created empty scene keeps a non-null global probe object
  where the Editor returns null. Identity beyond the retained-reference reload
  cases validated below remains unmeasured.
* Two component-deletion log lines retain their earlier ordering difference.
* Name/path/build-index lookups of pending or closing scenes, multiple-outgoing
  and held-load listing order, and editor root-scene counts remain unmeasured.

The two occlusion keys, eight SH values and four Gamma float-centre values remain
in `docs/COMPATIBILITY_GAPS.md`'s insignificant-residual ledger with all raw values
preserved. The earlier 56 introduced pixels were fixed and are now exact.
Unmeasured cases recorded above remain unmeasured. The 1024 handler-created-scene
limit is an explicit Unbound resource limit, not a measured Unity limit.

The reset-phase validation passes 1641/1641 managed tests and changes only creation
`seq20 /state/lightProbes`: `probes0 0 18 0 73` becomes Native's `probes0 18 18 73 73`.
Canonical remains 65/84 because that snapshot retains other gaps; strict failing leaves
drop from 22 to 21. Raw/teardown files, lifecycle, main probe and PNG are byte-identical.
The whole set is directly measured in one OnDestroy snapshot; keeping it throughout every
OnDisable and outgoing root's destruction is Unbound's chosen position in that window.
The multiple-outgoing, no-outgoing and exception controls pin implementation behavior,
not measured Unity rules. Reports: `validation/runs/lp-reset-phase-validation-20261102c/`;
Root admission: `validation/runs/lp-reset-phase-root-admission-20261103/root-validation.json`.

The retained-reference reload cases now return the earlier probe object with the
same counts. Matching validation on source `850614a1` resolves all 25 identity
readouts: creation canonical 65->76/84, strict failing leaves 21->10; lifecycle
14->21/31. These are 25 alias replacements within 18 raw JSON value strings. Every
other character, count and callback remains unchanged. Raw/teardown captures, the
625-row main probe and its PNG are byte-identical before/after; no match is lost.
The outgoing `seq20` reset snapshot stays fixed.

The implementation retains project Lighting Data identities by GUID and fileID,
with weak references to previously returned objects. Sharing one key across scene
files is an implementation mechanism, not a separately measured Native identity
guarantee. Replacing an unused object's data with freshly read data is an Unbound
preservation policy. Unreferenced-object identity depends on collection; persistence
of in-memory edits, duplicate-position combination, cross-scene shared-asset cases
and `GetInstantiatedLightProbesForScene` clone semantics remain open or unmeasured.
Empty-active-null, hull/query, additive async and dying-object gaps remain visible.

Validation: `validation/runs/lp-identity-matching-validation-20261104/`, 1694/1694
managed tests, source-only reuse-disabled red control 6 assertion failures/3 guards,
and six engine captures. API 0/2319 is reused through 65228 equal external metadata
rows. Root checks and admission: `validation/runs/lp-identity-root-admission-20261104/`.

### Q381 lookup and Q406 bounds and mesh scope

The current main comparison is 379/625, previously 375/625 after Q381 and 367/625 before it,
with no lost matches and an unchanged PNG. The six single-scene query-index keys match. The
combined-scene queries at point 6 return occlusion channel 1 as zero, previously about 0.655.
Q406 corrects query bounds rounding and resolves four occlusion-at-probe rows. The 246 remaining
strict differences split into the unchanged 228 numeric-only keys, at most
1.1920928955078125e-7 per component with mixed signs, and 18 functional keys:

* Twelve query-index keys in `additive tetrahedralized` and `unloaded`, at points 0, 1, 2, 3, 6
  and 8. The combined mesh's topology and order remain open.
* Four combined counts/shared-Main keys, 120 cells versus the reference's 121.
* Two `occlusion at probes` rows, `additive tetrahedralized` and `unloaded`. Combined probe 22's
  channel 1 remains zero versus one. Its boundary-cell choice changes which channels are
  present, even where a contributing vertex's weight is zero.

The walk accepts nonnegative weights exactly, chooses a later slot when minimum weights tie,
and stops a two-step cycle with the current weights. A renderer remembers its last found cell.
Public queries outside the positions' bounds return ambient; inside them they can use open hull
cells. The renderer's interpolation path continues to extrapolate through its hull cells.
The occlusion rule and bounds survive query snapshots and scene combinations. The importer
already carries the per-slot channels, so no cache output or cache format changes.

`Geometry/ProbeGeometry.cs` and `geometry.py` record independent diagnostic controls in
`Reference/unity-6000.0.71f1/geometry.jsonl`. Two normal GPU Editor exits must agree byte for byte
before publication. Extending this reference also checks every old row byte for byte.
The public `Lightmapping.Tetrahedralize` helper returns one cell for a tetrahedron, one after
removing its exact duplicate, no cells for the five coplanar points, two for the thin layout and
three for the exterior-point layout. The engine's builder produces those cell counts but retains
the duplicate position and has different index order. The diagnostic engine replay calls its
builder directly, independently of imported cells; it does not implement the public Editor
helper or `SetPositionsSelf`.

The face controls use 18 integer/dyadic positions with affine coefficients. At `(0.5,0.5,0)` the
reference returns 1.5; just inside at z=1e-6 it returns 1.500004; just outside at z=-1e-6 it returns
the ambient probe. Moving to `(0.5,0.5,0.25)` returns 2.5. Several starting hints exercise shared
faces, and renderer hints agree with unhinted results after the lighting changes. Complete index
answers remain in the strict comparison. Open-cell extrapolation and the outer bounds have
additional controls at `(1,1,1)` and `(2.01,1,1)`: the first returns 4.66666651 through an open
cell, the second returns ambient. The original 68-row diagnostic subset remains 37/68. Its 31 strict
failures are 16 query-index answers, four mesh index orders, the retained duplicate position,
and ten ambient-component values at most 3.725290298461914e-9 off. Every cell count and every
non-ambient lighting result matches. The ten ambient differences are numeric residuals in these
controls; the 21 index/position differences remain open, with no tolerance changes.

Q406 adds every starting cell for Main probe 8 and combined probe 22, and both forward and
reverse bulk-query orders. Every Main hint returns index -1, ambient lighting and occlusion one.
The bounds are stored as `(min + max) * 0.5` and `(max - min) * 0.5`, then tested through the
reconstructed minimum and maximum. In Main, this changes minimum y from 0.14000000059604645
to 0.1400001049041748. Probe 8 falls outside. This is a measured 6000.0.71f1 rounding rule,
not an epsilon expansion. Independent synthetic layouts repeat that result on each axis and
check a point 1e-6 inside, 1e-6 outside and an interior point. Managed tests also cover snapshots
and combinations. The engine keeps finite nearest-probe fallback available for invalid bounds.

Combined probe 22 remains inside the bounds. Different valid starting cells in the Editor can
return channel 1 as one, zero or a small signed rounding value; one captured walk returns zero
lighting. Both bulk orders return channel 1 as one in this layout. These observations do not
establish hint-invariant occlusion. The engine's forward bulk order still returns zero; this
remaining boundary-cell selection rule is open.

The extended visualization layouts measure duplicate tolerance independently of runtime
combination. A point 0.049 units from the first point is removed, while distances 0.05 and 0.051
are retained. In the chain at x=0, 0.04 and 0.08, only the first point survives. Checking against
all earlier input positions, including a discarded position, explains these measured answers;
checking only retained positions does not. The runtime builder does not implement this
visualization preprocessing, and shrinking its public probe arrays is not justified by this
Editor-only helper. Runtime duplicate coefficients and scene combination remain unmeasured.

The actual Main and combined positions also go through the visualization helper, preserving
all vertex indices. Main has 45 inner cells. The combined helper has 87 inner cells; the engine
has 86. An independent empty-circumsphere check identifies the missing cell as vertices
10, 11, 24 and 25 in the captured combined positions. Its circumsphere contains an artificial
enclosing vertex used by the current builder. The finite enclosure therefore suppresses a
valid cell. This rules out a pure index-order explanation for the 120-versus-121 total.
An arbitrary increase of the enclosure constant is not a general correction. Construction
with an unbounded hull, tetrahedron and hull-face order, vertex rotation, neighbour order and
search-start selection remain open. No layout-specific cell insertion or index remapping is used.
The observed construction uses incremental Delaunay insertion; deterministic randomized point
ordering alone does not determine final cell order because cell replacement and traversal matter.
Coplanar, duplicate and thin layouts retain their raw reference answers. These controls do not
establish general degeneracy parity.

The full extended comparison is 47/298, previously 45/298 on the same source and reference.
There are 250 differing keys and one missing key, `combined boundary seed 120`, because the
engine has one fewer cell. Ninety differing keys are ambient-only rounding, at most
3.725290298461914e-9. The other 160 contain mesh positions, counts, index order or combined
boundary SH/occlusion differences. `bounds axis 1 sample 3` also differs by
4.76837158203125e-7 in its coefficient of eight, alongside its index difference. Those raw
answers and the original tolerances remain unchanged. In particular, the 22 earlier cycle
controls and the original 68-row subset retain their Q381 results.

`mesh_diagnostics.py REFERENCE CAPTURE --report REPORT` independently enumerates empty
circumspheres using exact integer arithmetic on the recorded float32 coordinates. It reports
all missing and extra vertex sets, and any cospherical ties, separately from the strict index
comparison. Both actual layouts have no such ties. This diagnostic preserves the gap; it is
not a replacement verdict or an ordering normalization for `compare.py`.

The Extra bake also records the rounding-cycle position
(5.498985290527344,2.438500165939331,-0.5086919069290161) with affine coefficients and all 22
starting cells. Twenty-one hints return inner cell 1 with coefficient 10.8846769. Hint 10 returns
open cell 10 with coefficient zero. All 22 answers now match. An open-cell plane rejection writes
its own weight state (0,0,0,-1) before moving inward, so a two-step cycle never reuses weights
from the inner cell with different vertices. The earlier implementation returned 2.09406352 at
hint 10. This is a measured rounding corner case in 6000.0.71f1, not a claim that arbitrary hints
always produce identical lighting.

The managed controls also keep finite nearest-probe lighting available if a set contains a NaN
position and no cells. Nonfinite cached bounds do not reject the whole set. This preserves the
existing fallback; the Editor's treatment of invalid probe coordinates is not measured here.

Run the diagnostics separately from the admitted baked-lighting gate while mesh construction
and order remain open. The comparator reports every discrepancy and exits nonzero on any one:

```sh
python3 tests/projects/lightprobes/geometry.py editor --release 6000.0.71f1 --work <cache-run>
python3 tests/projects/lightprobes/geometry.py publish --release 6000.0.71f1 --work <cache-run>
UNBOUND_CLI=<snapshot>/unbound UNBOUND_MANAGED_DIR=<snapshot>/managed \
  python3 tests/projects/lightprobes/geometry.py unbound --release 6000.0.71f1 --work <cache-run>
python3 tests/projects/lightprobes/compare.py \
  tests/projects/lightprobes/Reference/unity-6000.0.71f1/geometry.jsonl \
  <cache-run>/unbound/geometry.jsonl --report <cache-run>/comparison.json
```

No index renumbering or layout-specific hull patch was admitted. A finite enclosing tetrahedron,
probe insertion order, cell vertex rotation, search-start choice and open-cell arithmetic all
remain relevant to a general replacement for the combined mesh builder. The diagnostic layouts
bound the current investigation; they do not establish general topology or degeneracy parity.

Unity 6000.0 contracts checked for this work:

* [LightProbes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbes.html)
  and [GetInterpolatedProbe](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbes.GetInterpolatedProbe.html):
  interpolation combines baked and realtime lighting; the renderer caches a search hint.
  The moving face controls and managed renderer test exercise a changed lighting value and cell.
* [CalculateInterpolatedLightAndOcclusionProbes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbes.CalculateInterpolatedLightAndOcclusionProbes.html):
  no baked probes gives ambient and occlusion one; output arrays may be omitted individually,
  and supplied arrays must fit. The existing main recorder and managed tests retain those controls.
* [Light Probes manual](https://docs.unity3d.com/6000.0/Documentation/Manual/LightProbes.html)
  and [moving GameObjects](https://docs.unity.com/en-us/engine/6000.0/manual/lighting-overview/direct-and-indirect-lighting/light-probes/moving-objects):
  probes provide spatially interpolated lighting. The exact cell order and tie rules here are
  measured observations, not documented ordering guarantees.
* [LightProbeProxyVolume](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightProbeProxyVolume.html):
  a spatial grid supplies lighting gradients across large objects. Grid sampling is outside this
  change; it is not established by the single-position renderer controls.

* [Lightmapping.Tetrahedralize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping.Tetrahedralize.html)
