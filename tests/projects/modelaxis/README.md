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

Two independent GPU Editor captures agree byte for byte for each set (1001, 397, 1467 and
11839 keys), published under `Reference/unity-6000.0.71f1/pivots`, `skinned`, `rigged` and
`reduced`. These sets bound each
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
| [Renderer.localBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-localBounds.html) | A SkinnedMeshRenderer's default local bounds are precomputed from the model's animations; Update When Offscreen recomputes them every frame. | In 6000.0.71f1 only Legacy rigs include their clips (`Rigged` set): Generic and None rigs keep the import-pose bounds with clips that bend the rig. |

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
- `Rigged` set: Legacy rigs add the vertices skinned by each sampled frame of each clip, from
  the first key in steps of 1/sampleRate summed in single precision while not past the last key
  (`LegacySwing` at 30 fps misses the 1 s frame, `LegacySwing60` reaches it), in the root bone's
  space (`LegacyRootOnly` keeps the import-pose box). Generic (`GenericSwing`) keeps the
  import-pose box, and so does the Humanoid rig type (`HumanoidSkin`: the humanoid probe's
  HumanA with boxes skinned to its bones; its avatar does not configure, the Editor reports no
  LeftFoot, so the control shows the rig type's rule, not a playing humanoid). Rig None imports
  the mesh without skinning under a MeshRenderer and places its node like any other
  (`NoneSwing`, `NoneMeshRp`). A bone whose rest pose (the first take's start) has a zero scale
  on it or an ancestor gets an all-zero bind pose: a zero-scale root bone gives a zero box
  (`GenericZeroRoot`), and a Legacy clip scaling the root bone up from zero turns Update When
  Offscreen on (`LegacyRootVanish`; with it on, `localBounds` is the renderer's own update and
  is not recorded: Unity answers a zero box there before any update). A vertex no cluster
  weights skins like the others in `BakeMesh` and adds nothing to Legacy bounds
  (`*Unweighted`; the import-pose box also skips it).
- A skinned mesh's node with unequal pivots, a rotation or scaling offset, or children
  (`MeshRp`, `MeshSp`, `MeshRpSp`, `MeshRoff`, `MeshSoff`, `MeshMaya`, `MeshChild*`): its
  rotation and scaling pivots are dropped as in `MeshPivot`, its rotation offset is ignored, and
  with `c` = minus its scaling offset it moves by `R·S·c` while its vertices and children move
  by `-c`, as for other nodes.

### Implementation scope and limits

`crates/unbound-assets/src/model/pivots.rs` and `skinned_bounds.rs` implement these rules; the
model cache format is v34. Clips of pivoted nodes take positions with the same placement at
every frame, then the clip's keyframe reduction (`Reduced` set below). Not measured,
implemented by the same rule or left as before:

- `localBounds` of blend-shape meshes without bones, a configured humanoid avatar, rig None
  with blend shapes, and which of several shared instances is "first" beyond the measured
  parent-before-child order.
- Blend shape deltas on meshes with geometric transforms (transformed with `G`'s linear part).
- A LODGroup's size and reference point come from mesh bounds, which now include blend shape
  frames; LOD levels with blend shapes are unmeasured.

### Keyframe reduction (`Reduced` set)

Each control is the column rig with a clip of four keys per curve (Hips moves, Spine turns and
scales, Head turns): Legacy under Keyframe Reduction with the default errors (`Default`), looser
(`Loose`: 3 degrees, 2%), tighter (`Tight`: 0.2 degrees, 0.1%) and unequal position and scale
errors (`Split`: 3%, 0.1%), at 60 fps (`Rate60`); Generic under Keyframe Reduction
(`GenericReduced`) and Optimal (`GenericOptimal`); three seconds of slow straight motion where
segments reach the 50-frame limit (`Drift`); the pivoted `AnimPivot` take of the Pivots set
reduced; and Legacy under Keyframe Reduction and Compression (`Packed*`, `LegacyOptimal`, below). The probe samples every clip of each model (`AnimationClip.SampleAnimation`) each
1/60 s over its length. The [Animation tab](https://docs.unity.com/en-us/engine/6000.0/manual/assets-and-media/asset-types/models/importing/class-animation-clip)
documents the errors (rotation as an angle in degrees, position and scale as a percentage of
the original value, tested per axis and on the whole vector) but not which keys are tested; the
measured rule, applied to the every-frame keys of compression Off (positions with linear,
rotations and scales with smooth slopes):

- A curve that the segment between its first and last keys with flat slopes holds everywhere
  becomes those two keys. Otherwise keys are dropped from the start while the segment from the
  last kept key to the next one, with their own slopes, holds the every-frame curve at each
  frame, at each dropped key and halfway between, spanning fewer than 50 frames.
- "Holds": rotations within the error in degrees (normalized dot product) with the segment's
  quaternion within 0.001 of unit length; positions, scales and floats per component and in
  length within the error as a fraction of the curve's value (near-zero values aside).
- Kept keys keep their every-frame slopes.
- Float curves (blend shape weights) take the position error and smooth slopes
  (tests/projects/blendshapes: its reduced clip now matches within 2e-6). Humanoid clips are
  converted to muscle curves, which the Editor reduces; their transform curves keep every frame
  here and the muscle curves' reduction is not reproduced.

A first attempt with 0.05 degrees and 0.05% put the rotation threshold within a few single
precision steps of a unit dot product, where keeping or dropping a key is a rounding tie
(our captures differed by up to 0.0009 in basis vectors there); `Tight` uses errors above that.

Key times: a key a whole number of frames after the range's start lies at that number times
the frame length, both in single precision (3 s at 30 fps: 3.0000002 for rotations; the
position curve of `Drift` ends at 3 exactly, scales unmeasured). With these times a segment of
exactly 50 frames is not "fewer than 50": `Drift` keeps frame 49. Legacy clips, like Generic
ones, have curves only for the channels the take animates (`Packed`: the unanimated rotation of
Hips is not packed), pivoted nodes aside.

#### Legacy rotation packing (`Packed*`, `LegacyOptimal`)

The page says Keyframe Reduction and Compression affects only file size, but Legacy clips
imported with it (or with the value 3, Optimal for the other rig types: `LegacyOptimal` equals
`Packed`) play rotations up to 0.6 degrees from the reduced keys; positions and scales are
unchanged, Generic clips are not affected (`GenericOptimal`). After the reduction each
rotation curve is stored packed and plays what unpacking returns:

- key times cut off to whole hundredths of a second (`PackedRate60`, `PackedRate120`: frames
  closer than that; `PackedDrift`: 1.6333 s becomes 1.63);
- three quaternion components cut off to 10 bits over -1..1 (9 bits for the component after the
  largest), the largest in magnitude rebuilt from them with its sign (`PackedTurn`: past half a
  turn the largest component changes and turns negative);
- slopes rounded to 6 bits over the range the curve's slopes span.

Controls: default errors (`Packed`), looser (`PackedLoose`), 60 and 120 fps, the 3 s clip and
more than half a turn in one second. The keys `AnimationUtility.GetEditorCurve` returns for
these clips agree with the imported ones to 5e-9 in value and exactly in time (slopes to 4e-6,
as for unpacked clips). Not measured: clips whose first key lies before zero, ranges that start
or end between frames, curves with different incoming and outgoing slopes (the importer makes
none for rotations), kept keys that fall into the same hundredth (above 100 fps; `PackedRate120`
keeps none) and the hidden preview clips, which are packed like the others. The key times above
change a result only in the 3 s Legacy clips here; Generic and compression Off clips longer
than a second take the same times unmeasured. Humanoid clips (the transform keys their muscle
curves are converted from, and the curves of nodes outside the human skeleton), float curves
(blend shape weights) and root motion curves still take the plain difference of times.

`PackedRate120` uses a scale error of 1%: at 0.5% dropping its scale corner at 1/3 s is a tie
(the segment over it misses the corner by 0.5% of its value to within 4e-6), and the Editor,
whose sampled key beside the corner is one single-precision step above ours (1.48750007
against 1.4875), drops the corner where we keep it: scales differ by 0.0075 for two samples,
both reductions within the error. A reduction exactly on its limit follows the last step of the
sampled values, which are not reproduced to that step.

```sh
python3 tests/projects/modelaxis/Tools/make_pivots.py
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Rigged
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Reduced
python3 tests/projects/modelaxis/run.py editor --release 6000.0.71f1 --set Pivots --label <fresh>
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Pivots
python3 tests/projects/modelaxis/run.py unbound --release "$UNBOUND_PROBE_UNITY_VERSION" --set Skinned
```

With the same comparator the previous engine matched 909/1031 `Pivots` keys (17 of 38 models
failing, including a helper GameObject for `GeoShared`) and 383/402 `Skinned` keys (9 of 10
models failing: no imported local bounds, a helper node under a pivoted bone); now all 1001 and
397 keys pass.
