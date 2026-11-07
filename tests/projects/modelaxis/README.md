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

## Recovery status

The last completed release build, at `d952d1f64`, passed all 720 original keys and 178/180
signed controls. The two remaining signed keys are `U1S1B0H0/Branch/Tip/worldVertices` and
`U1S1B0H1/Root/Branch/Tip/worldVertices`, each at vertex 3, component X: Native -81.65851,
engine -81.6585. Their complete raw comparisons remain under `as3-signed-normalized`.
No comparator or reference was changed. All 114 asset-crate tests passed. The existing model
regression results above are from `as3-regress`.

Commit `da7d6e8bf` applies quaternion normalization consistently to static FBX nodes, including
those two unchanged-axis controls. Its release build did not complete, and the managed-test run
ended without a TRX or terminal summary. These runs are not passes. Final build, all three
engine-only probe commands, managed/Rust validation, the independent Sol-high review and
`tools/agents/land.py` remain required. The latest build/test logs are
`~/.cache/unbound/agents/logs/as3-static-rotation-build.log` and `as3-managed-tests.log`.
Work stopped with approximately 150 MiB free on the data filesystem and 440 MiB on root;
all captures and original failures are preserved. Free regenerable build/cache space before
resuming validation. No engine or probe outputs may be removed as part of that cleanup.
