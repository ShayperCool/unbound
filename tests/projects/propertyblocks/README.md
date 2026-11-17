# Property blocks changed after they were set

`Assets/Scripts/PropertyBlockProbe.cs` draws, with an unlit shader that outputs `_Color`, a quad
under a renderer's block (`Renderer.SetPropertyBlock`), a two-submesh quad pair whose second
material slot has its own block (`SetPropertyBlock(block, 1)`) and a terrain whose material
template uses the same shader, under a splat block (`Terrain.SetSplatMaterialPropertyBlock`).
Each step changes the blocks or the materials, waits a frame, renders a camera looking down
into a 4×1 linear render texture and records the colour at each place (`<step>.drawn.*`, bytes)
and what `GetPropertyBlock` / `GetSplatMaterialPropertyBlock` and `HasPropertyBlock` return
(`<step>.get.*`, `<step>.has.*`). The project is in the linear colour space.

Steps: nothing set (`none`); each block set (`set`); the same block objects edited without
setting them again (`edited`); set again (`setAgain`); set again unchanged (`setUnchanged`);
cleared, filled and set again, as a block reused every frame (`refilled`); refilled without
`_Color` (`withoutColor`); the materials changed under those blocks (`materialChanged`); the
materials changed under blocks with `_Color` (`materialUnderBlock`); a renderer block on the
renderer that has a slot block, then edited (`rendererAndSlot`, `rendererAndSlotEdited`); a block
edited and every block cleared (`cleared`); set again after clearing (`setAfterClear`).

Measured in the Unity 6000.0.71f1 Editor (two identical captures, NVIDIA driver):

* All three setters copy the block's values when called. An edit of the block draws, and is
  returned by the getters, only once the block is set again. The 6000.0 Scripting API documents
  the copy for Renderer.SetPropertyBlock and Graphics.RenderMesh
  ([MaterialPropertyBlock](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.html):
  "The block passed to Graphics.RenderMesh or Renderer.SetPropertyBlock is copied"); for
  [Terrain.SetSplatMaterialPropertyBlock](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Terrain.SetSplatMaterialPropertyBlock.html)
  the copy is measured only.
* A block without `_Color` draws the material's `_Color`, also after the material changes, and a
  material change under a block with `_Color` is not seen. This holds for a terrain's material
  template too.
* A slot's block replaces the renderer's for that slot; the other slot draws with the
  renderer's block.

Not measured: other renderer types (skinned, sprite, particle, line), SRP draws, blocks on
`Graphics.RenderMesh` and command buffers, textures and arrays in the blocks, a terrain's base
map after its template is edited (the probe draws no base map; Unbound makes it again).

    python3 tests/projects/propertyblocks/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/propertyblocks/run.py record --label B
    python3 tests/projects/propertyblocks/run.py publish A B
    UNBOUND_CLI=<immutable runtime>/unbound python3 tests/projects/propertyblocks/run.py gate
