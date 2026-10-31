# Model import probe

What the model importer gives a game, for FBX files exported by Blender (`make_models.py`) and
written by hand in ASCII (`make_ascii_models.py`: Lambert, texture transforms, Bump and the property
sets of 3ds Max, Maya Arnold and Autodesk Interactive materials). `ModelImportProbe` instantiates
every model of Assets/Models and records each GameObject's components, every renderer's material
slots (shader, render queue, tags, keywords, GI flags, every shader property, textures by name and
size with offset and scale, the material's asset path, which slots share a Material) and the
LODGroup the importer adds. `DescriptionDump` (Assets/Editor) records the material descriptions
the importer hands `OnPreprocessMaterialDescription`.

The same file appears under several importer settings (only the `.meta` differs):
`materialImportMode` None, "Standard (Legacy)" and "Import via MaterialDescription"; the legacy
external location with `materialName` by texture, material, model + material and texture or
model + material, and `materialSearch` local, recursive up and everywhere (the Editor created the
Material assets under the `Materials` folders on import; they are part of the probe). Textures are
found by relative path, in the model's folder and its `Textures` folder, in the `Textures` folders
above, and by name anywhere (Search.fbx); an embedded texture is not extracted. Captures run in
Gamma and Linear projects.

    python3 tests/projects/modelimport/run.py editor --label <L>    # two Editor runs per color space
    python3 tests/projects/modelimport/run.py publish --label <L>   # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> python3 tests/projects/modelimport/run.py unbound --label <L>
    R=tests/projects/modelimport/Reference/unity-6000.0.71f1 W=~/.cache/unbound/validation/runs/modelimport/<L>
    python3 tests/projects/modelimport/compare.py $R/Gamma.jsonl $W/unbound/Gamma.jsonl
    python3 tests/projects/modelimport/compare.py $R/Linear.jsonl $W/unbound/Linear.jsonl
    python3 tests/projects/modelimport/compare.py $R/descriptions.txt $W/unbound/Gamma.descriptions.txt

After changing a generator, regenerate the models (`blender -b --factory-startup --python
make_models.py`, `python3 make_ascii_models.py`) and let the Editor import them and remake the
scene: `Unity -batchmode -nographics -quit -projectPath tests/projects/modelimport -executeMethod
ProbeRunner.Create`.

What Unity 6 does (the reference): "Import via MaterialDescription" runs the built-in
`FBXMaterialDescriptionPreprocessor` (Standard, or Autodesk Interactive for those exporters'
materials) and slots without a material get Default-Material; the legacy importer makes Standard
materials from the diffuse colour (no factor, no colour-space conversion), transparency, diffuse,
normal and emissive textures, and a "No Name" material for slots without one. Every renderer node
named `*_LOD<n>` (case sensitive) goes to level n of one LODGroup on the root (level nodes without a
mesh of their own make none); level i switches at 0.25 / 2^i of the screen height and the last at
0.01; its size and reference point are the levels' bounds. The root gets an Animator only with an
avatar (`avatarSetup` 1, the Avatar named `<model>Avatar`); `avatarSetup` 0, the default of current
metas, gives none even to animated or skinned models. Metas that omit a setting take the current
defaults.

What still differs: an Autodesk Interactive material with a mask threshold has `_DstBlend` 0 in the
Editor although its preprocessor sets 10 (queue 3000 stays; when the Editor loads it outside Play
mode the queue reads 2450, as the shader's GUI would set it); Unbound keeps 10. Material animation
curves are not imported.
