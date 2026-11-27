# What a model file that does not read imports as

What the AssetDatabase answers for model files whose content the model importer cannot read, the
callbacks and log lines their `ImportAsset` gives, and the same for files that read with nothing in
them. Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two identical captures).

`Assets/Models` holds `Garbage.fbx` (a line of text), `Empty.fbx` (no bytes), `Truncated.fbx` (a
binary FBX cut after its header), `CutAscii.fbx` (an ASCII FBX cut inside its header block),
`CutModel.fbx` (an ASCII FBX of two connected meshes cut inside its connections), `CutToken.fbx` and
`CutLine.fbx` (the same cut inside a number and at a line end before its connections),
`CutBinary.fbx` (a binary FBX cut mid-file), `Lfs.fbx`, `Zeros.fbx` and `Html.fbx` (a Git LFS
pointer, 4096 zero bytes, an HTML page), `RigGeneric.fbx`, `RigHumanoid.fbx` and `RigLegacy.fbx`
(text, their `.meta` asking for a Generic or Humanoid avatar or a Legacy animation),
`Garbage.obj` (text), `Empty.obj` (no bytes) and `Tri.obj` (one triangle, the control). `Assets/Editor/ModelPostprocessor` notes
`OnPreprocessAsset`, `OnPreprocessModel`, `OnPostprocessModel` (with the root's name and child
count) and `OnPostprocessAllAssets` for them.

`Assets/Scripts/BrokenModelProbe.cs` records in Play mode into values.jsonl in `$BROKENMODEL_OUT`
(tests/value_probe.py), per model: the main asset type and importer, the loaded main object (type,
name, children, components), the `GameObject` and `Mesh` loads, the types `LoadAllAssetsAtPath`
gives, the representations, the root's and its Transform's fileIDs; then a forced `ImportAsset` with
its callbacks and log lines (`<model>/import.events`, sorted; the project's own path written
`<project>`) and the same queries again (`<model>/after/*`). Then `Fresh.fbx`, text written in the
session without a `.meta`, is imported by `ImportAsset`, changed and refreshed (`fresh/*`), and the
text `.fbx`'s root is instantiated.

What it shows: the Editor never fails such an import. The model is an empty root named after the
file with what the `.meta`'s rig adds (an Animator and an Avatar, a Legacy Animation), the same
fileIDs as any model's root, and every import callback runs. An `.fbx` that is no FBX (text, no
bytes, zero bytes, an LFS pointer, HTML) logs `ImportFBX Errors:` / `Couldn't read file <path>.` /
`None of the registered readers can process the file`; a binary one cut short (after its header or
mid-file) `Couldn't import file <path>.` / `File is corrupted: '<name>'`. An ASCII `.fbx` cut short
reads up to the cut without an error, keeping the nodes connected to the root by then (none
before its connections), and an empty or text `.obj` reads as an empty model without one. A model whose import logs anything gets an
`ImportLog` sub-asset.

`residuals.json` names what Unbound does otherwise.

Record and gate:

```bash
python3 tests/projects/brokenmodel/run.py record --label <label>   # twice, then publish one
python3 tests/projects/brokenmodel/run.py gate
```
