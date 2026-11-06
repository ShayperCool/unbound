# Imported material bindings

Release under test: Unity 6000.0.71f1, built-in renderer, Gamma color space.

| Source | Documented guarantee or limitation |
|---|---|
| [6000.0 Materials tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Materials.html) | None uses the default diffuse material. Standard and MaterialDescription create materials differently; embedded materials can be remapped. MaterialDescription ignores diffuse color when a diffuse texture is set. These controls have no textures. No imported slot sort order is specified. |
| [6000.0 materialImportMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-materialImportMode.html), [importMaterials](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-importMaterials.html) and [import modes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporterMaterialImportMode.html) | Import mode selects material generation; this does not specify face or slot ordering. The boolean API has been removed in favor of materialImportMode. Its description says false substitutes the default material; true finds or creates materials using the naming/search settings. |
| [6000.0 Mesh.subMeshCount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-subMeshCount.html) | A submesh corresponds to a renderer material; vertices can be shared across submeshes. |
| [6000.0 Mesh.GetIndices](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetIndices.html) | Returns vertex indices for the specified submesh, with base vertex applied by default. Triangle topology returns multiples of three. |
| [6000.0 Renderer.sharedMaterials](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-sharedMaterials.html) | All materials used by the renderer. The returned array is a copy; replace the array to change bindings. Shared material edits affect all users. |
| [6000.0 model formats](https://docs.unity3d.com/6000.0/Documentation/Manual/3D-formats.html) | FBX and OBJ are supported. Exported information can be checked by reimporting into a DCC application. This does not promise identical material serialization or naming between formats. |

`make_slot_models.py` authors four separate quads with per-face bindings. Declaration order is
Red, Green, Blue, Unused. Declared first uses 0,1,2; FirstUse first uses 2,0,1; Sparse uses only
2 and 0. Instances shares one FBX geometry between two nodes and changes the node bindings.
SharedNames has two distinct FBX materials named Shared; SharedNamesReverse first uses the other
one. ObjectsObj repeats the material set in
two OBJ objects/groups. Multi copies the existing lightmap UV source. Import, Calculate and None
normal variants hold optimization and generated UV2 off.

The runtime captures each renderer's submesh count, raw indices, vertices, normals, material
names, colors and identity sharing. OBJ models explicitly remap names to authored Material assets,
isolating slot assignment from MTL property conversion. Images draw mesh-local geometry with the
renderer bindings, isolating imported axis/pivot transforms. They use the bound colors on unlit materials, then apply red/green/blue
by slot index. The latter observes the concrete effect of a script overriding a material slot.
The full image and value comparisons are exact, and every differing value and pixel is retained
in `slot-comparison.json`. Nothing sorts or permutes the captured mesh or material arrays.

Two independent GPU Editor runs must have byte-identical JSON and PNG captures before
publication. Only the new material profile is published, under the existing release directory;
the earlier modelimport and lightmapuvs oracles remain untouched.

```sh
python3 tests/projects/modelimport/material_slots.py editor --release 6000.0.71f1 --label AS1
python3 tests/projects/modelimport/material_slots.py publish --release 6000.0.71f1 --label AS1
UNBOUND_CLI=<cli> python3 tests/projects/modelimport/material_slots.py unbound --release 6000.0.71f1 --label AS1
```

## Measurements and cause

Both independent Native repeats are byte-identical for all 306 values and 20 PNGs. FirstUse
imports Blue, Red, Green; Sparse imports Blue, Red and drops unused slots. The second instance
binds the same geometry to Red, Green, Blue through its own node material set. Duplicate names
reuse the first used embedded material: SharedNames uses red for both Shared bindings and
SharedNamesReverse uses green. Slot identities confirm the alias; nodes with the same material
set share those material objects. Multi imports Slot1, Slot2, Slot0.

Unbound previously iterated material declarations. It now uses ufbx's first-face-use material
parts for both face assignments and renderer bindings. It also reuses the first used embedded
material with the same name. Face traversal, vertex generation, normal arithmetic and triangle
optimization are unchanged. Model cache version 21 prevents reuse of the previous binding data.
These are measurements of the captured release and contexts, not a documented global ordering
contract. Other releases, material postprocessors that rename assets, and conflicting same-name
materials across differently ordered node hierarchies remain unmeasured.

The initial development captures also exposed white OBJ materials without remaps and a rendered
orientation difference for the authored FBX axis setting. Those raw captures and complete reports
remain in the validation cache. They are separate follow-ups, outside this material-slot gate.
The canonical profile uses explicit OBJ remaps and mesh-local draws, and retains every raw
difference in that profile. Earlier modelimport and lightmapuvs references are unchanged.

## Validation

The pre-fix runtime matches 240/306 values and 13/20 images. With the fix all 306 values and
20 images match exactly. The engine-only suite runs this profile as part of modelimport.
The existing lightmap normal gate now includes Multi: 208/264 cases exact, 56 recorded float
residuals, zero failures. Comparing the old and new lightmap captures changes only 20 submesh
arrays; vertices, normals, UVs and UV2 are unchanged. The UV2 result remains 362/384.

Before/after runs of modelimport, blendshapes, baked, humanoid, modelclips and lightmapuvs show no
functional or image regression. Existing modelimport captures are byte-identical, including
their 755 raw differences. Humanoid retains 203 differences; modelclips retains 31391/31391
matching values; blendshapes retains 41/41 records plus its expected difference. Baked and blend
image measurements are unchanged. Raw logs, captures and reports are under the validation cache,
not in Git. No comparator tolerance or existing reference changed.
