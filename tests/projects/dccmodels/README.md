# Model files of DCC formats

What a model file of a DCC application's format imports as. Unity 6000.0.71f1, recorded with the
macOS Editor in batch mode (two identical captures), with Blender 5.2.2 installed. The Editor
imports a `.blend` file by running the installed Blender (`/Applications/Blender.app` on macOS) as
`blender -b <file.blend> -P <Editor>/Tools/Unity-BlenderToFBX.py -- <out.fbx>` and reading the
FBX file it writes with the FBX importer; Unbound does the same with the Editor installation's own
script. The values depend on the Blender version that converts the file (another version's
exporter may write other values), not on the OS or the graphics device.

`Assets/Models/Scene.blend` (authored with Blender 5.2.2: an empty `Root` turned and moved, a
child `Box` cube turned and scaled with two materials on alternate faces, a smooth-shaded `Ball`
sphere) has the `.meta` the Editor writes for it, with `isReadable` on so the probe can read its
meshes in Play mode. `Assets/Scripts/DccModelsProbe.cs` records in Play mode into values.jsonl in
`$DCCMODELS_OUT` (tests/value_probe.py); numbers compare within 1e-4:

* `model.<file>.importer`, `.globalScale`, `.useFileScale`, `.fileScale`: its importer.
* `model.<file>.main`, `.assets`: the main asset and every asset the file holds.
* `model.<file>.node.<path>.*`: each node's local position, Euler angles and scale, components,
  materials, mesh and children.
* `model.<file>.mesh.<name>.*`: each mesh's vertex and sub-mesh counts, each sub-mesh's topology
  and indices, bounds, channel counts, and every vertex's position, normal and first UV.

Residual (residuals.json): `ModelImporter.fileScale` is a generated stub for every model format.
Not recorded: a machine without Blender (the Editor cannot import the file either; Unbound reports
it as not imported), the Linux Editor's Blender lookup (Unbound runs `blender` from the `PATH`),
and the other DCC formats (`.dae`, `.3ds`, `.max`).

Record and gate:

    python3 tests/projects/dccmodels/run.py record --label a   # twice, then
    python3 tests/projects/dccmodels/run.py publish a b
    python3 tests/projects/dccmodels/run.py gate
