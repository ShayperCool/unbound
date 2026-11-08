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
Gamma and Linear projects and in a URP project (the urp probe's pipeline asset and packages),
with GPU Editors (private Xvfb, VirtualGL); `MODEL_IMPORT_PROFILES=URP` limits a run.

    python3 tests/projects/modelimport/run.py editor --label <L>    # two Editor runs per color space
    python3 tests/projects/modelimport/run.py publish --label <L>   # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> python3 tests/projects/modelimport/run.py unbound --label <L>
    R=tests/projects/modelimport/Reference/unity-6000.0.71f1 W=~/.cache/unbound/validation/runs/modelimport/<L>
    python3 tests/projects/modelimport/compare.py $R/Gamma.jsonl $W/unbound/Gamma.jsonl
    python3 tests/projects/modelimport/compare.py $R/Linear.jsonl $W/unbound/Linear.jsonl
    python3 tests/projects/modelimport/compare.py $R/URP.jsonl $W/unbound/URP.jsonl
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
defaults. In URP, URP's preprocessors replace the built-in one's materials (Standard's values stay
for names Lit declares too), the shader's ShaderGUI validates them (blend states, queue control,
GI flags), and slots without a material get URP's Lit.mat; the legacy importer uses URP's shaders.

What still differs: an Autodesk Interactive material with a mask threshold has `_DstBlend` 0 in the
Editor although its preprocessor sets 10 (queue 3000 stays; when the Editor loads it outside Play
mode the queue reads 2450, as the shader's GUI would set it); Unbound keeps 10. Material animation
curves are not imported. URP: the legacy importer's URP materials are not reproduced (Unbound
makes Standard ones); `GetTag("RenderType", false)` reads "" for the Editor's validated Lit
materials, "Opaque" in Unbound; materials of Shader Graph shaders keep EmissiveIsBlack and an
explicit queue where the Editor's generated shaders leave the queue to the shader.

## Smoothing and take ranges

`make_smoothing_models.py` writes `Assets/Resources/Smoothing`: ASCII FBX grids with a smoothing
layer in every encoding (polygon masks, an index array, a version 101 flag layer, per corner, per
control point, all same, an array shorter than the faces, an FBX 6.1 file), edge smoothing with
sharp edges meeting in L, T and cross junctions, a loop with a tail, scattered sparse and dense
sharp edges, the dense pattern with the edge list reversed and given from the other face, takes
whose stack has only `LocalStop`, only `LocalStart`, neither, both reversed, both equal, only the
reference span, a single key or no curves, and OBJ files with numbered, `on`, `off`, negative,
wide and fractional smoothing groups, groups interleaved with two materials, and every MTL key the
importer reads (`illum`, `Ka`, `Kd`, `Ks`, `Ns`, `d`, `Tr`, `Tf`). Every model imports with
calculated normals and mesh optimization off. `SmoothingProbe` records each mesh's vertices,
normals, submesh triangles and materials (by hierarchy order) and every clip's length and frame
rate; the material descriptions are dumped as in the main probe.

    python3 tests/projects/modelimport/smoothing.py editor --release 6000.0.71f1 --label <L>
    python3 tests/projects/modelimport/smoothing.py publish --release 6000.0.71f1 --label <L>
    UNBOUND_CLI=<unbound> python3 tests/projects/modelimport/smoothing.py unbound --release 6000.0.71f1 --label <L>

What the Editor does (6000.0.71f1, two identical captures in `Reference/unity-6000.0.71f1/smoothing`):
- FBX smoothing comes from the smoothing element of layer 0. Layer versions before 102 hold
  flags, later ones group masks; `ReferenceInformationType` and an index array are ignored. The
  mapping spreads values over face corners (missing values are 0, `ByVertice` needs one value per
  control point, an unknown mapping gives 0 everywhere). Edge smoothing becomes polygon groups by a
  depth-first walk over smooth edges in edge order; a face with a hard edge into its own region
  leaves it, and smooth edges to earlier regions give both faces a further shared bit. Sharp edges
  meeting in L/T/cross junctions or loops therefore stay sharp unless the walk's order re-joins
  them (the fixtures record which).
- OBJ `s <integer>` sets the group (`s 1.5` is 1, `s -1` all bits), `s off` sets 0, `s on` and
  other words keep the current group; groups are bit masks. Faces are ordered by group (unsigned,
  negative groups last), stable within a group, before materials, control points and triangles are
  numbered. A mesh whose faces are all group 0 smooths by angle.
- A take's span is its stack's `LocalStart`/`LocalStop` (0 where missing, in order; the
  reference span is not used); spans shorter than single precision's epsilon use the key range of
  the stack's curves of two or more keys. Each property keeps its keys inside the span; the clip
  ends at the last key kept. A take whose keys all lie outside the span gives an empty clip
  (length 1, as any clip without keys); a take without curves gives no clip.
- MTL: `illum` below 2 makes a Lambert description, otherwise Phong; `Ka`/`Kd` (and `Ks` for Phong)
  set the colour and its factor to 1, `Ns` the shininess exponent; `d` gives transparency 1 − d with
  a white transparent colour, else `Tr` gives the factor with `Tf` (or white) as colour, else `Tf`
  alone the colour with factor 1. These rules live in `unbound_compat::MtlRules`.
