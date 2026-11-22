# Baked lightmap layout probe

Unity 6000.0.71f1, built-in render pipeline. Where `Lightmapping.Bake` in Edit mode puts each
renderer of a scene in its baked lightmaps: its lightmap index and lightmap scale and offset, and
each lightmap's size. Unity documentation (6000.0):
[Lightmapping.Bake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping.Bake.html),
[Renderer.lightmapScaleOffset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-lightmapScaleOffset.html),
[Lighting Settings (Lightmapping settings)](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightingSettings.html),
[Mesh Renderer (Scale In Lightmap)](https://docs.unity3d.com/6000.0/Documentation/Manual/class-MeshRenderer.html),
[Lightmap Parameters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LightmapParameters.html).
The documentation gives no packing rule: the layout here is the release's observed one.

## Content

`Assets/Editor/AtlasSetup.cs` (`bake_atlas.py create`) makes the scenes of `Assets/Scenes`, each
with its own Lighting Settings (Progressive CPU, one sample, no filtering, no compression,
non-directional):

- `Spread` (10 texels per unit, 1024, padding 2): every primitive, placed, turned and scaled;
  Scale In Lightmap 0.5, 2 and 0; renderers the Editor leaves out (not static, lit by probes,
  disabled, inactive); children of a scaled, turned parent; a LOD Group with a renderer in two
  levels; positions far away and beyond -65536.
- `Crowded` (20, 256, 2): 48 cubes of five sizes, several lightmaps.
- `Oversize` (40, 128, 4): a plane clamped to the lightmap's size and small cubes.
- `Fixed` (10, 512, 2): Fixed Lightmap Size.
- `Tags` (10, 256, 2): Lightmap Parameters differing in each field that keeps atlases apart
  (Baked Tag 1 and 2, Anti-aliasing Samples, Pushoff, Backface Tolerance) and in a realtime field
  only.
- `Meshes` (10, 512, 2): meshes without lightmap UVs, with lightmap UVs off the unit square,
  narrow ones, a vertex no triangle uses, two submeshes.
- `Ties` (10, 256, 2): equal cubes whose positions share a Morton code, made in another order
  than their names.

## Capture and comparison

`Assets/Editor/AtlasRecord.cs` opens each scene, bakes it and records every MeshRenderer's
lightmap index and the bits of its scale and offset, and the lightmaps' sizes. The Editor runs it
in batch mode on the GPU runner (`bake_atlas.py native`), with its `Data/Tools` folder on the
library path (its LightBaker and denoiser plug-ins load from there by name). On this machine the
Editor's LightBaker process sometimes crashes (exit code 11) whatever the scene; the recorder
bakes again then (the layout is made before it). Two byte-identical captures make the reference
(`Reference/unity-6000.0.71f1/atlas.jsonl`).

Unbound does not make the lightmaps yet: its bake writes the layout to
`Library/Unbound/BakedGI/<scene GUID>/atlas.json` (`Graphics/BakedAtlas.cs`), from which
`bake_atlas.py engine` makes the candidate. A renderer without a lightmap (an index outside
0..65533 in the Editor, absent from the layout in Unbound) is `none`. Every record is compared
exactly (`bake_atlas.py check`).
