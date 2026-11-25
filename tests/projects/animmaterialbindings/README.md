# animmaterialbindings

The `material.*` bindings `AnimationUtility.GetAnimatableBindings` offers for a renderer, in the
Unity 6000.0.71f1 Editor's order (Play mode, two identical Linux batch Editor captures;
`Assets/Scripts/AnimMaterialBindingsProbe.cs`). Three probe shaders: `Q758/Forward` and
`Q758/Reverse` declare every property type (Float, Range, Int, Integer, Color, HDR Color, Vector,
2D, Cube and 3D textures, `[NoScaleOffset]`, `[HideInInspector]`, `[PerRendererData]`) in opposite
orders under names of their own; `Q758/Builtin` mixes built-in names (`_MainTex`, `_Color`,
`_Cutoff`) with new ones. Each is recorded on a SpriteRenderer and a MeshRenderer, then a material
with values set for names its shader lacks, a MeshRenderer with two materials, one whose shader was
replaced, and both renderers' default materials.

Measured: for each material in slot order, its float properties, then its vector properties
(colours, vectors and each texture's `_ST`, `_TexelSize` and `_HDR`), each kind in the order of
Unity's property name ids, not the declaration order; Integer properties are not listed; values a
script set for names the shader does not declare are, but not those a replaced shader's defaults
left (nor the stale entries of the built-in Sprites-Mask material, tests/projects/animbindings); a colour has `r, g, b, a` components, an HDR colour
and every vector `x, y, z, w`; one `m_Materials.Array.data[i]` binding per slot. Unity's engine
gives a fixed id to names it knows before any shader loads (`_MainTex` 7, `_Color` 177, ...) and
numbers a shader's other names when it loads the shader, in its declaration order (a texture's
`_ST`, `_TexelSize`, `_HDR` right after it). The order in which the Editor loads a project's
shaders is not one the probe can set, so no two probe shaders share a name. The capture writes the
session's ids beside the values (`ids.txt`, not compared): the probe's names, every loaded
shader's names, and with `ANIM_MATERIAL_BINDINGS_CANDIDATES` (a file of names) those names' ids;
`ANIM_MATERIAL_BINDINGS_FIND_ORDER` finds the shaders in another order first, which changes no id.

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/animmaterialbindings/run.py record --label a
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/animmaterialbindings/run.py record --label b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/animmaterialbindings/run.py publish a b
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/animmaterialbindings/run.py gate
```
