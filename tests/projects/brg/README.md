# BatchRendererGroup probe

A BatchRendererGroup in the shader graph probe's URP scene (tests/projects/shadergraph, staged with
unsafe code allowed and the BatchRendererGroup variants kept; its Lit sphere stays as an ordinary
renderer), built as the Manual's "Creating a renderer with BatchRendererGroup" does: one batch in a
GraphicsBuffer of the device's `BatchRendererGroup.BufferTarget` (a raw buffer, or a constant
buffer given to AddBatch with its window), a zeroed block at address 0, packed float3x4
`unity_ObjectToWorld` / `unity_WorldToObject` and per-instance `_BaseColor` flagged `0x80000000`.
The culling callback emits four draw commands in one range: eight URP Lit cubes, two URP Unlit
spheres, and two mirrored cubes (negative x scale), one with `FlipWinding` and one without.

`Assets/Scripts/BrgProbe.cs` renders the main camera twice into a 320x180 texture per scenario
and writes `<scenario>.png` and `values.jsonl`: the API's answers (buffer target, window size, the
IDs of registered meshes, materials and the batch) and every callback's context (view type,
projection, masks, planes, splits, LOD parameters, view ID, the light it is for), and per group and
view type how many callbacks ran. Scenarios:

- `basic`: the four commands above;
- `shadows`: the sun casts hard shadows in two cascades (the URP asset's count set at run time)
  onto a ground plane; the range casts and receives shadows, and a second group takes light views
  alone (`SetEnabledViewTypes`) and draws nothing. Two more ranges draw pillars, one on layer 3,
  which the sun's culling mask leaves out (the sun does not light it, yet it casts a shadow), one on
  layer 4, which the camera's mask leaves out (not drawn, yet it casts a shadow);
- `cameras`: a second camera elsewhere renders the group; its culling mask leaves out the pillar's
  layer, so it does not draw the pillar;
- `lifetime`: the batch removed and added again and the cube mesh unregistered by both its owners
  and registered again (their new IDs), then the cubes' colours changed between the two renders
  (the second draws them); `swap`: SetBatchBuffer to another buffer of other colours;
- `errors`: AddBatch windows the device refuses (`errors.add`: the IDs and the Editor's messages,
  which differ by device), then two more commands, of an unknown mesh and of `Probe/NoDots`
  (`Assets/Shaders`, SRP Batcher compatible, without DOTS instancing): neither is drawn. The Editor
  logs an error for each once per run, delivered after the frame that drew them, so the probe,
  which records within that frame, does not record them;
- `forward_plus`: the renderer switched to Forward+ at run time (`UniversalRendererData.renderingMode`)
  under six point and two spot lights: URP's clustered light data (tile and z-bin buffers, the
  additional lights and reflection probes) are constant buffers the Lit program reads beside the
  group's DOTS instancing data and the scene's ordinary Lit sphere; `forward_plus_plain`: the same
  with the group drawing nothing and eight ordinary Lit cubes in its cubes' places. URP's shader
  library (`Input.hlsl`) sizes those buffers for 256 lights on Vulkan (the tile buffer alone 64 KB)
  and for 32 on OpenGL Core;
- `local_shadows`: a spot light and a point light cast hard shadows (URP's additional light shadows
  turned on at run time, the sun casting none) onto the ground plane, with the light-only group as
  in `shadows`; each light culling's context also names its light. Each light is culled once with
  all its splits (the spot light one, the point light its six faces), the light as the view at
  slice -1 (a directional light's is 0), every split's culling matrix the light's projection by
  view: a point light face's with its vertical axis upright, and on Vulkan, whose depth is
  reversed, its depth axis turned too.

- `sorting`: transparent commands (URP Unlit, alpha blended, half-transparent quads facing the
  camera) whose drawing order shows in the colour where they overlap (`sorting.command<i>.pixel`):
  two commands flagged `HasSortingPosition` whose positions contradict their order; unflagged
  pairs with `sortingPosition` 1 and 1000, 1000 and 1, 0 and 0; flagged commands of two instances
  (near first, far first) and an unflagged one; unflagged commands over an ordinary transparent
  renderer at the same place (distance about 6.5) with approximate depths from -1000000 to
  1000000; flagged commands one unit nearer or farther than such a renderer, and a flagged
  command of two instances on both sides of one. The Editor sorts a flagged command's instances
  one by one at their world positions among the other draws; an unflagged command sorts as if
  minus its `sortingPosition` were its squared camera distance (nearer than the renderer from
  -20 up, farther at -50 and below), equal values in command order, its instances in their order.
  `sorting_ortho`: the same through an orthographic camera framing the quads alike (size 6 tan of
  half the field of view), the c5 depths from -50 to -2: an unflagged command sorts as if minus its
  `sortingPosition` were the renderer's view depth, 6 (nearer from -5 up, farther at -7 and
  below; the depths do not separate it from the plain distance, 6.46 to 6.59), the orthographic
  camera's own distance metric (TransparencySortMode.Orthographic); flagged commands sort at their positions
  as through the perspective camera.

- `split_masks`: the sun casts hard shadows in two cascades onto a large ground; four pillars
  near the camera, their shadows in the first cascade, are drawn by commands of
  `splitVisibilityMask` 1, 2, 0 and 3 (`split_masks.p<i>.body`, `.shadow`: the pillar's colour and
  the ground's where the shadow of a point near its top falls). The camera draws every pillar;
  only masks 1 and 3 cast a shadow there. `split_masks_second`: the same with the cascades split
  at 1/20 of the shadow distance, so the shadows fall in the second cascade: only masks 2 and 3
  cast. URP makes one shadow list per cascade from the same settings, whose split index is
  unset (-1), so the light's lists take its splits in the order they are made.
- `lightmap_arrays`: lightmap texture arrays as Entities Graphics draws them: eight white Lit cubes
  in a row above the scene's sphere, each its own command over a batch that adds per-instance
  `unity_LightmapST` and `unity_LightmapIndex`. Their materials are copies of Lit carrying
  `unity_Lightmaps`, `unity_LightmapsInd` and `unity_ShadowMasks` (2x2 RGBAHalf arrays of three
  slices, a colour per slice and texel), with LIGHTMAP_ON, without it, or with LIGHTMAP_ON and
  DIRLIGHTMAP_COMBINED. A zero lightmap scale samples the texel the offset names; the last cube's
  scale reads its lightmap UVs. Cubes 3 and 6 are not flagged IsLightMapped, cubes 4 and 6 have
  no LIGHTMAP_ON. `lightmap_arrays.c<i>.body`: each cube's colour at its centre. The Editor lights
  cube 3 (keyword, no flag) from its slice and cube 4 (flag, no keyword) like cube 6, by the
  ambient probe: the material's keyword decides, not the flag.
- `gpu_written`: instance data a compute shader writes, as Entities Graphics uploads it: a raw
  buffer (given to AddBatch with a window on OpenGL Core, which reads constant buffers) holding
  the eight cubes' matrices and a grey colour from SetData, then `Assets/Shaders/Resources/BrgWrite.compute`
  writes the first four cubes' colours (`gpu_written.c<i>.body`). The batch ID differs by device
  (9 on OpenGL Core, 8 on Vulkan): the `errors` scenario's AddBatch calls make one more batch on
  OpenGL Core (`errors.add.twiceMaxWindow`).

- `procedural`: commands without a mesh and indirect commands of other topologies, one per cell
  of a grid in front of the scene, each drawing two instances (`Assets/Shaders/DotsProcedural.shader`:
  DOTS instanced, no vertex attributes; vertex id v lies at corner v % 4 of a square shifted along
  x by v / 4, so the ids a draw reads show in the image). `procedural.c<i>.pixels` counts the
  pixels of cell i that differ from the scene drawn without the commands (`procedural_background`),
  `.drawn` whether any does. Procedural commands (`BatchDrawCommandProcedural`): triangles of six
  vertex ids, the same with base vertex 6 (drawn as without it: a non-indexed command's ids start
  at 0), 16-bit indices from a byte offset, 32-bit indices with base vertex 4, lines without and
  with indices, points, a line strip and quads (the device's own primitive: points on OpenGL
  Core, a triangle list over the ids on Vulkan). Procedural indirect commands
  (`BatchDrawCommandProceduralIndirect`): their counts, first vertex or index from an arguments
  buffer, the instances from a visible instance buffer. Indirect commands of the cube's indices as
  triangles, lines and points and of a line square's as lines. A procedural indirect command whose
  visible instance buffer window starts at byte 256 reads its instances there on OpenGL Core,
  whose buffers are constant buffers read through a window (`c16`; `c17`, the instances it would
  read at the buffer's start, stays empty); on Vulkan, whose buffers are raw, the Editor refuses a
  window (`BatchRendererGroup had non-zero ConstantBuffer mode parameters when BufferTarget is
  RawBuffer. ...`, in its log for every list that reads the command) and does not draw it; the
  probe's other indirect commands pass no window there.

- `procedural_graphics`: the same kind of shapes without a batch, as controls for the quads rule
  (`Assets/Shaders/ProceduralIds.shader`: the ids placed as DotsProcedural's, no DOTS instancing):
  `Graphics.RenderPrimitives` of six ids as two triangles (`c0`), and eight ids as quads through
  `Graphics.RenderPrimitives`, `DrawProcedural`, `RenderPrimitivesIndirect` and
  `DrawProceduralIndirect` (`c1` to `c4`), four ids as quads (`c5`), queued for the camera before
  each of its two renders, over `procedural_background`; `procedural_graphics.log<i>` lists what
  the renders log. On both devices alike, direct draws make a quad of each four ids (two squares
  for eight), while the indirect ones are not drawn: each logs the error `DrawProceduralIndirect
  can't draw quads if platform does not support quad topology` once a render (4x for the two
  draws over two renders).

Light contexts record their receiver planes again as `receiver<i>`, compared exactly.

    python3 tests/projects/brg/run.py native --label L [--device vulkan]   # two Editor captures
    python3 tests/projects/brg/run.py publish L-1 L-2 [--device vulkan]
    python3 tests/projects/brg/run.py check [--device vulkan]              # the land gate

The references (`Reference/unity-6000.0.71f1/opengl`, `.../vulkan`) are two identical captures each
of the Editor on its OpenGL Core and Vulkan devices, measured on the NVIDIA driver only. The
Editor draws the batch on both devices (the Manual's platform list names only Vulkan for Linux);
the images are identical on both, and so are the values but the buffer target, the AddBatch windows each device refuses and the `procedural` scenario (its lines' and points' pixels, its quads and its windowed command; `procedural_graphics` is the same on both). Unbound: every value on both devices (2584 records on OpenGL Core, 2581 on Vulkan), the cascades'
caster culling planes, culling matrices and conservative culling spheres of the light contexts
included; every image matches exactly.

## Metal (macOS)

`--device metal` records the macOS Editor in batch mode, which draws with Metal
(`tests/macos_editor.py`); `Reference/unity-6000.0.71f1/metal` holds two byte-identical
captures on an Apple M2 Pro. Against the Linux references every Metal image differs by one
step in most pixels (means 0.46 to 0.98, worst 1) in the macOS Editor and in Unbound alike: that
is Metal's own rounding, which the device decides; the device also decides the batch buffer
target (RawBuffer, as on Vulkan) and the refusals that follow from it. So on macOS every check
compares Metal with the Metal reference at the same bounds (`check` and `check --device vulkan`
alike, each saying so). The Metal values match Vulkan's (2301/2326, the rows before the `procedural` scenario) apart from `device` and a one-step
rounding of read-back colours (`lightmap_arrays.c5.body` and 23 `sorting*` pixel rows). The
`procedural` and `procedural_graphics` scenarios are not recorded on Metal yet: `residuals-metal.json` lists their rows until
the macOS Editor records them.
