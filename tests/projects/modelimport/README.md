# Model import probe

What the model importer gives a game, for FBX files exported by Blender (`make_models.py`) and
written by hand in ASCII (`make_ascii_models.py`: Lambert, texture transforms, Bump and the property
sets of 3ds Max, Maya Arnold and Autodesk Interactive materials). `ModelImportProbe` instantiates
every model of Assets/Models and records each GameObject's components, every renderer's material
slots (shader, render queue, tags, keywords, GI flags, every shader property, textures by name and
size with offset and scale, the material's asset path, which slots share a Material) and the
LODGroup the importer adds. `DescriptionDump` (Assets/Editor) records the material descriptions
the importer hands `OnPreprocessMaterialDescription`. `MaterialStages` (Assets/Editor) records the
Exotic models' materials between the render pipeline's preprocessors (`stages.*`: after its FBX
preprocessor, after its Autodesk Interactive one, after its Arnold and 3ds Max Physical ones: the
shader, GI flags, whether the shader has `_EmissionColor`, its `_QueueControl`). With URP the
Editor validates a material with its new shader's editor as a preprocessor sets the shader: Lit's
validation sees the emission and GI flags the Editor's own FBX preprocessor (order -990) set, and a
Shader Graph shader's queue control is validated before the next preprocessor. The recorder has
no `OnPostprocessMaterial`: the `URPHookRecord` profile (`Reference/.../URPHookRecord.jsonl`) is the
URP one with `Hook/MaterialPostprocessRecord.cs` in `Assets/Editor`, whose `OnPostprocessMaterial`
records each Exotic material there (`stages.*.postprocess.*`), leaves the other materials
untouched and changes nothing itself. Reading a material's property values in that callback runs
its shader editor's validation, so the legacy importer's transparent Exotic URP materials end
opaque (queue 2000, no `_ALPHAPREMULTIPLY_ON`, Opaque), while MaterialsLegacy/Cube_Alpha, given to
the callback without a read, stays transparent (the control). Outside this reference, in repeated
Unity 6000.0.71f1 Editor captures, an empty callback and ones reading only the shader, the GI
flags or `shaderKeywords` changed nothing; ones calling only `HasProperty`, only `GetFloat` or
only `GetColor` made the change. Writes in the callback are not measured.

The `BuiltinBlend` profile seeds conflicting blend factors and depth writes in a late
`OnPreprocessMaterialDescription` callback. It records queue, tag, keywords and GI flags
before and after property access, writes, rendering, or `SetPass` in Play mode. The controls
cover all four modes, fractional and out-of-range modes, copies, and a project shader that
uses Standard's shader editor. Use `MODEL_IMPORT_PROFILES=BuiltinBlend` to record or replay
the 223 observations.

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
GI flags), and slots without a material get URP's Lit.mat. The legacy importer makes its
materials with URP's shaders from the start (Lit's own defaults, not Standard's), sets the main
colour and texture beside `_Color`/`_MainTex`, leaves Lit's blend values alone, tags an opaque
material with a diffuse, normal or emission texture Opaque (the untextured ones read no
RenderType; every measured textured one also has a normal map or emission, so a material with
only a diffuse texture is unmeasured, as is a pipeline without Autodesk Interactive shaders,
whose Autodesk Interactive materials Unbound converts like the others), and gives Autodesk Interactive materials URP's Autodesk Interactive shaders with the
file's values (base colour, emission, roughness and metallic zero when absent; opacity, mask
threshold, UV offset with V negated and UV scale when present), with no blend mode, keyword,
queue or GI flag, validated by the shader's editor as the material is made (before those values;
`_QueueControl` 0).

The Autodesk Interactive cutout's preprocessor sets `_DstBlend` to 10 and its saved queue
to 3000. The first property value access in Play mode validates the imported material through
its shader editor. The cutout then reads `_DstBlend` 0 and queue 2450. Standard and Autodesk
Interactive use the integer part of the blend mode. An out-of-range mode keeps the imported
blend and queue values while still updating emission flags. In `BuiltinBlend`, reading a tag
keeps the saved metadata until the first property read. A write to a float property keeps
the value supplied by the caller, including a new mode used during validation.

Material animation curves remain unsupported. URP: every value matches (7807/7807). The Editor validates a material
each time a preprocessor sets its shader and once more after the last one: the description-made
Shader Graph materials keep Lit's GI flags (Lit's validation, as URP's FBX preprocessor sets Lit,
keeps `RealtimeEmissive` without `EmissiveIsBlack`) and the Autodesk Interactive cutout the
`_Color` Lit's first validation gave it, while the materials that stay Lit take `_Color` from the
base colour; the legacy importer's Autodesk Interactive materials have `_QueueControl` 0 from the
validation as they are made (`stages.*`). The description-made Arnold Maya,
Autodesk Interactive, Physical and Autodesk Interactive cutout materials keep the shader's queue
(rawRenderQueue -1, queue 3000, 2450 for the cutout, RenderType "Transparent" for the three
transparent ones), in Unbound too since a shader change leaves a material only the new shader's
names, values set from script included (tests/projects/materials `scripted.*`): URP's own
description preprocessor sets Lit's `_Surface`, then the Arnold, Physical and Autodesk
Interactive preprocessors give the material a Shader Graph without it, whose editor reads no
surface type and keeps the shader's queue (before, it read the stale opaque `_Surface` and stored
queue 2000 and RenderType "Opaque": 11 values).

The URP reference is recorded as an Editor that has drawn its views before Play mode: the runner
renders one camera before entering Play mode, so the configured pipeline's shader tag is active
as the scene loads (as in tests/projects/graphqueue, the case Unbound plays). A batch-mode Editor
that enters Play mode without a render validates the materials without that tag
(`GetTag("RenderType", false)` then reads "" for the validated Lit materials, and the transparent
description-made materials read queue 2000); the Gamma and Linear captures are the same either
way.

## Smoothing and take ranges

`make_smoothing_models.py` writes `Assets/Resources/Smoothing`: ASCII FBX grids with a smoothing
layer in every encoding (polygon masks, an index array, a version 101 flag layer, per corner, per
control point, all same, an array shorter than the faces, an FBX 6.1 file), edge smoothing with
sharp edges meeting in L, T and cross junctions, a loop with a tail, scattered sparse and dense
sharp edges, the dense pattern with the edge list reversed and given from the other face, takes
whose stack has only `LocalStop`, only `LocalStart`, neither, both reversed, both equal, only the
reference span, a single key, no curves, a stop between keys or two layers, and OBJ files with numbered, `on`, `off`, negative,
wide and fractional smoothing groups, groups interleaved with two materials, a named group
without materials under the legacy import, and every MTL key the
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
  mapping spreads values over face corners (missing values are 0, `ByVertice` of the wrong count is
  ignored, an unknown mapping such as `ByVertex` gives 0 everywhere, edge arrays of the wrong
  length cover the edges they reach). Edge smoothing becomes polygon groups by a
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
  the curves of two or more keys in the stack's last layer. Each property keeps its keys inside the span; the clip
  ends at the span's end or at the last key of the curves that overlap it, whichever is earlier
  (a stop between keys ends the clip there). A take whose keys all lie outside the span gives an empty clip
  (length 1, as any clip without keys); a take without curves gives no clip.
- MTL: `illum` below 2 makes a Lambert description, otherwise Phong; `Ka`/`Kd` (and `Ks` for Phong)
  set the colour and its factor to 1, `Ns` the shininess exponent; `d` gives transparency 1 − d with
  a white transparent colour, else `Tr` gives the factor with `Tf` (or white) as colour, else `Tf`
  alone the colour with factor 1. These rules live in `unbound_compat::MtlRules`.
