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
under `Reference/unity-6000.0.71f1`. Repeats, Editor projects, original failures and all engine
captures remain under the validation cache. Labels are immutable; use a fresh label for each run.

## Contract and cause table

All links below are official Unity 6000.0 documentation.

| Source | Documented guarantee or limitation | Measured context / implication |
| --- | --- | --- |
| [Model tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html) | Scale Factor applies a global scale; Convert Units converts the file's scaling. Bake Axis Conversion modifies asset data when enabled, otherwise the root Transform compensates. Preserve Hierarchy retains an explicit prefab root. | Capture both settings and both root representations; compare mesh-local and world data. |
| [bakeAxisConversion](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-bakeAxisConversion.html) | Unity uses left-handed, Z-forward, Y-up coordinates; conversion acts on geometry/animation or the hierarchy root depending on this setting. | Static geometry only. The exact signed mapping of the authored axis metadata is an observation in 6000.0.71f1, not a general contract for every exporter. |
| [useFileScale](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-useFileScale.html), [globalScale](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-globalScale.html) | File scale is optional and global scale controls import scaling. | Both vertex positions and nested translations are measured, avoiding a compensating mesh/root scale hiding an error. |
| [useFileUnits](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-useFileUnits.html) | This setting applies only to `.max` files and exists for old exporter compatibility. | It is not the FBX Convert Units setting. The probe varies `meshes.useFileScale` instead. |

Baked and unbaked captures retain their observed signed mappings independently. The docs do
not specify Euler decomposition, floating point rounding, or this ASCII file's exact signed
mapping. Pivots, geometric transforms, pre/post rotation, additional handedness/up-axis systems,
negative scales, animated/skinned models and other releases remain separate measurements.
