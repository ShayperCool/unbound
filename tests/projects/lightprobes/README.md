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

* Outside the probes' hull every interpolation query, with or without a renderer, gives the
  ambient probe, occlusion (1, 1, 1, 1) and tetrahedron index -1.
* A probe's occlusion is a vector by shadow mask channel: the occlusion of the mixed light baked
  into each channel, 1 where no light is; it is blended with the SH weights.
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

No `creation-ownership.jsonl` Reference is published for 6000.0.71f1 yet. The one pair run so far
(both repeats exit 0, gated) differed in 11 order-only leaves: which outgoing roots were already
destroyed when the destroyer's `OnDestroy` ran, the name order in Unity's "Some objects were not
cleaned up when closing the scene" error, and the destruction order of the four leftover objects at
the next Single load. The schema would need to declare those orders unordered before a new pair can
publish. Every existence, scene, parent/root, `Find`/`FindObjectsByType` and light probe value
agreed between the two repeats.
