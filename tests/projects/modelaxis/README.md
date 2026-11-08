# Static FBX axis and units

This probe isolates coordinate conversion from pivots, pre/post rotation, animation, skinning,
materials, mesh optimization and normal generation. `Tools/make_project.py` authors ASCII FBX
and explicit Unity-compatible `.meta` settings. The tetrahedron has four unequal axis extents;
its ancestors have translation, rotation and nonuniform scale. No probe changes a loaded
model's transform. The 32 controls combine Y-up/Z-up, centimetres/metres, Bake Axis Conversion,
Convert Units and Preserve Hierarchy, with global scale 2.5.

`AxisProbe` records hierarchy paths, local position/scale/rotation basis, world position/basis,
mesh vertices, triangle indices, bounds and transformed world vertices. It gates every key and
array element; indices and hierarchy are exact, numeric geometry has a fixed absolute 1e-5
bound chosen before the first capture. Every missing, extra or failing value stays in
`comparison.json` outside Git. The engine gate does not start the Editor.

```sh
python3 tests/projects/modelaxis/run.py editor --release 6000.0.71f1 --label as3-initial
python3 tests/projects/modelaxis/run.py publish --release 6000.0.71f1 --label as3-initial
UNBOUND_CLI=/path/to/unbound python3 tests/projects/modelaxis/run.py unbound --release 6000.0.71f1 --label before
```

Two independent GPU Editor project copies agree byte-for-byte on all 720 keys before publication
under `Reference/unity-6000.0.71f1`. Eight further controls reverse the source front-axis
sign, with independent agreeing Native repeats and 180 new keys in the `signed/` reference.
Pass `--set Signed` to each driver mode to record or gate these controls. The original
720-key reference is unchanged. Repeats, Editor projects, original failures and all engine
captures remain under the validation cache. Labels are immutable; use a fresh label for each run.

## Contract and cause table

All links below are official Unity 6000.0 documentation.

| Source | Documented guarantee or limitation | Measured context / implication |
| --- | --- | --- |
| [Model tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html) | Scale Factor applies a global scale; Convert Units converts the file's scaling. Bake Axis Conversion modifies asset data when enabled, otherwise the root Transform compensates. Preserve Hierarchy retains an explicit prefab root. | Capture both settings and both root representations; compare mesh-local and world data. |
| [bakeAxisConversion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-bakeAxisConversion.html) | Unity uses left-handed, Z-forward, Y-up coordinates; conversion acts on geometry/animation or the hierarchy root depending on this setting. | Static geometry only. The exact signed mapping of the authored axis metadata is an observation in 6000.0.71f1, not a general contract for every exporter. |
| [useFileScale](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-useFileScale.html), [globalScale](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-globalScale.html) | File scale is optional and global scale controls import scaling. | Both vertex positions and nested translations are measured, avoiding a compensating mesh/root scale hiding an error. |
| [useFileUnits](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-useFileUnits.html) | This setting applies only to `.max` files and exists for old exporter compatibility. | It is not the FBX Convert Units setting. The probe varies `meshes.useFileScale` instead. |
| [Quaternion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Quaternion.html) | Unity expects normalized quaternions. | Casting a unit f64 source quaternion to f32 can lose unit length. Static imported node rotations are normalized after conversion, including the unchanged-axis controls; this removes the measured world-geometry bias. |

Baked and unbaked captures retain their observed signed mappings independently. The docs do
not specify Euler decomposition, floating point rounding, or this ASCII file's exact signed
mapping. Pivots, geometric transforms, pre/post rotation, additional handedness/up-axis systems,
negative scales, animated/skinned models and other releases remain separate measurements.

The baseline engine matches 376/720 original keys. It ignores Bake Axis Conversion, and its
unbaked improper-axis decomposition creates negative scales and wrong world geometry. The
file-unit scaling matches in these controls. Raw before captures and full comparisons remain
in the validation cache under `as3-before` and `as3-signed-before`.

## Implementation scope

Static baked imports map the source axis permutation into geometry and every node's local
translation, rotation and scale. Unbaked improper-axis imports apply the measured root
translation and proper rotation without synthesizing negative scales. Right-handed unbaked
imports keep their existing axis-conversion path. All static FBX node quaternions are normalized
after f64-to-f32 conversion. The model cache format is v23.

Animated, skinned, blend-shape and vertex-cache baked imports now fail with an asset-named
unsupported-content error; their conversion needs separate reference probes. Unbaked animated
imports keep the existing implementation and are not certified here. The signed-permutation
implementation for unmeasured coordinate systems is an implementation choice, not an added
Unity guarantee.

`Tools/regress.py --release 6000.0.71f1 --label <fresh-label>` reruns the existing material-slot,
OBJ material, UV2 and normals comparators with their original references, tolerances and
expected source lists. The AS3 regression capture retains 306/306 exact slot values and 20/20
exact images; MTL 2702/2702 values and 60/60 images within 8/255, 40 exact; UV2 362/384;
normals 208/264 exact plus the same 56 listed residuals and no gated failures.

## Validation and recovery

The recovery release build passes all 720 original keys and all 180 signed keys at the
unchanged absolute 1e-5 bound. Consistent static quaternion normalization resolves the two
previous signed world-vertex failures. All 114 asset-crate tests and all 3110 managed tests
passed on the recovery base; `gen-stubs` built the handwritten API without stubs and reproduced
the committed generated files without drift. The landing gate repeats validation after rebase.

The engine-only commands are:

```sh
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION"
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Signed
python3 tests/projects/modelaxis/Tools/regress.py --release "$UNBOUND_PROBE_UNITY_VERSION" --label <fresh-label>
```

Set `UNBOUND_CLI` to the release CLI for all three commands. Recovery captures use labels
`as3-recovery-axis-20261107`, `as3-recovery-signed-20261107` and
`as3-recovery-regress-20261107`. They preserve the regression counts above. Build, stub,
managed and Rust logs are under `~/.cache/unbound/agents/logs/as3-recovery-*-20261107.log`.

The earlier build at `d952d1f64` matched 720/720 original keys and 178/180 signed keys.
The failing keys were `U1S1B0H0/Branch/Tip/worldVertices` and
`U1S1B0H1/Root/Branch/Tip/worldVertices`, vertex 3, component X: Native -81.65851,
engine -81.6585. Their complete raw comparisons remain under `as3-signed-normalized`.
The first attempt to build consistent static normalization stopped on storage exhaustion;
that interrupted build and managed run were not counted as passes. All prior captures,
failures and independent Native repeats remain preserved. No reference or comparator changed.

## Node transforms: pivots, geometric transforms and skinned bounds

`Tools/make_pivots.py` authors the `Pivots` and `Skinned` sets as ASCII FBX with explicit
metas; `Tools/blender_models.py` exports their Blender-convention files (`BlenderTree`,
`BlenderApply` with Apply Transform, `BlenderRig`), which are committed. Each `Pivots` control
puts one feature on the middle node `Body` of `Top`/`Body`/`Leaf`, where `Body` has rotation
and nonuniform scale: rotation and scaling pivots and offsets, pre/post rotation with and
without `RotationActive`, rotation order, geometric translation/rotation/scaling (also negative,
on a null and on a geometry two nodes share), inherit types RrSs/RSrs/Rrs, pivots on the root
(with and without Preserve Hierarchy), Maya-style combinations (pivots, offsets, joint orient as
pre-rotation, segment scale compensation as Rrs) and 3ds Max-style Z-up files with object
offsets, each also with Bake Axis Conversion. `AnimPivot` is a legacy clip on pivoted nodes
(Animation Compression Off) that the probe samples at 0.5 s and 1 s. `MayaNoIndex` and
`GeoAllNoIndex` leave out `DefaultAttributeIndex`, which exporters always write.

The `Skinned` set binds a three-bone column with mixed weights (bind pose equal to or away
from the file's node pose, the mesh node moved, a pivot or pre-rotation on a bone, a pivot or
geometric transform on the mesh node) plus the Blender-exported skinned models of
`modelimport` and `blendshapes` (`isReadable` on and mesh optimization off in these copies).
It records bones, root bone, bind poses, `Mesh.bounds`, `SkinnedMeshRenderer.localBounds` with
Update When Offscreen off, and `BakeMesh` vertices. Normals, bones, bind poses, skinned and
sampled keys exist only in these sets, so the `Axis` and `Signed` references are unchanged.

Two independent GPU Editor captures agree byte for byte for each set (1001 and 397 keys),
published under `Reference/unity-6000.0.71f1/pivots` and `skinned`. These two sets bound each
value by 1e-5 times the largest magnitude of its innermost reference vector (at least 1e-5):
world bases of
a centimetre-scaled root reach 100, where 1e-5 is below one float32 step. This was chosen
after the first captures showed 1-4 float32-step differences in such bases; the `Axis` and
`Signed` bound is unchanged.

### Contract and observations

| Source | Documented | Measured in 6000.0.71f1 / implication |
| --- | --- | --- |
| [Importing models from 3D applications](https://docs.unity3d.com/6000.0/Documentation/Manual/HOWTO-ImportObjectsFrom3DApps.html) | Every application section: "All nodes with position, rotation and scale; pivot points and names are also imported". For Maya files it lists Rotate Axis (pre-rotation), Joint Orient and Segment Scale Compensate as unsupported. Geometric transforms are not mentioned. | FBX pre/post rotation is applied to the node rotation (`Pre`, `Post`, `PrePost`; ignored without `RotationActive`). Inherit types other than RSrs are ignored (`Inherit0`/`1`/`2`), consistent with Segment Scale Compensate being unsupported. |
| [Model tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html) | Bake Axis Conversion, Preserve Hierarchy, Sort Hierarchy By Name. | Pivots and geometric transforms compose with baked axis conversion (`*Bake` controls) and with an explicit root (`RootPivotH`). |
| [Renderer.localBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-localBounds.html) | A SkinnedMeshRenderer's default local bounds are precomputed from the model's animations; Update When Offscreen recomputes them every frame. | Measured only where every animation pose is the import pose (no clip, or transform curves holding the import transforms, as in `BlenderBlob`). |

Observed rules (one reference capture each; not documented guarantees):

- A node keeps its rotation (pre-rotation, rotation, inverse post-rotation, in its rotation
  order) and scale. With `c` = RotationPivot - ScalingOffset, its GameObject sits at `M·c`
  for the FBX node matrix `M`, and its children move by `-c`. Its mesh is baked as
  `G·v - c` with the geometric transform `G`, which keeps every vertex and child where the FBX
  places it. No extra GameObjects are created.
- Normals take only the rotation of `G` (columns normalized, all negated for a negative
  determinant); geometric scaling leaves them unchanged and a negative scale keeps the winding.
- A geometry shared by several nodes is baked once with its first node's `G` and `-c`
  (`GeoShared`, `GeoSharedLeaf`, `PivotShared`).
- Without `DefaultAttributeIndex` the node is still placed at `M·c` but the vertices stay as in
  the file.
- A node holding a bone-skinned mesh is placed at its transform without its pivots, its vertices
  take only `G`, and its bind poses follow (`MeshPivot`, `MeshGeo`); pivots on bones follow the
  general rule (`BonePivot`).
- A baked handedness-reversing axis conversion turns each triangle before vertices are split,
  so split vertices are numbered in the turned order (`MaxZupBake`).
- `Mesh.bounds` includes every blend shape frame's positions. `localBounds` is, per bone, the
  box of the vertices the bone influences (with their blend shape frame positions) in its bind
  space, carried as a box into the root bone's space by the bone's import pose; the union over
  bones.

### Implementation scope and limits

`crates/unbound-assets/src/model/pivots.rs` implements these rules; the model cache format is
v26. Clips of pivoted nodes sample positions with the same placement on every frame. Not
measured, implemented by the same rule or left as before:

- `localBounds` of models whose clips move transforms (no `m_AABB`; the managed fallback and its
  unsupported-bounds warning remain), and of blend-shape meshes without bones.
- Children of a pivoted skinned-mesh node, unequal pivots or offsets on such a node (warnings),
  vertices without bone weights (they add nothing to `localBounds`), and which of several shared
  instances is "first" beyond the measured parent-before-child order.
- Blend shape deltas on meshes with geometric transforms (transformed with `G`'s linear part).
- A LODGroup's size and reference point come from mesh bounds, which now include blend shape
  frames; LOD levels with blend shapes are unmeasured.
- Clips of pivoted nodes sample positions on every frame whatever the compression; a
  keyframe-reduced clip therefore keeps more position keys than Unity's.
- Keyframe-reduced clips: a first `AnimPivotReduced` capture (compression Keyframe Reduction)
  matched the uncompressed pose in the engine but differed in the Editor by up to 0.0005 in
  basis vectors and 7e-5 m in position at 0.5 s, on unpivoted rotations too. That is the clip
  reduction approximation, not pivot handling; it stays a follow-up and is not in the set.

```sh
python3 tests/projects/modelaxis/Tools/make_pivots.py
python3 tests/projects/modelaxis/run.py editor --release 6000.0.71f1 --set Pivots --label <fresh>
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Pivots
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Skinned
```

With the same comparator the previous engine matched 909/1031 `Pivots` keys (17 of 38 models
failing, including a helper GameObject for `GeoShared`) and 383/402 `Skinned` keys (9 of 10
models failing: no imported local bounds, a helper node under a pivoted bone); now all 1001 and
397 keys pass.
