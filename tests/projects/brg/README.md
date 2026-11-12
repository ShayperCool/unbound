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
  and for 32 on OpenGL Core.

Light contexts record their receiver planes again as `receiver<i>`, compared exactly.

    python3 tests/projects/brg/run.py native --label L [--device vulkan]   # two Editor captures
    python3 tests/projects/brg/run.py publish L-1 L-2 [--device vulkan]
    python3 tests/projects/brg/run.py check [--device vulkan]              # the land gate

The references (`Reference/unity-6000.0.71f1/opengl`, `.../vulkan`) are two identical captures each
of the Editor on its OpenGL Core and Vulkan devices, measured on the NVIDIA driver only. The
Editor draws the batch on both devices (the Manual's platform list names only Vulkan for Linux);
the images are identical on both, and so are the values but the buffer target and the AddBatch windows each device refuses. Unbound: 727/851 values (OpenGL Core), 724/848 (Vulkan)
match, the others are listed in `residuals-<device>.json` (the cascades' caster
culling planes, culling matrix and spheres of the light contexts); `basic`, `cameras`, `forward_plus` and
`forward_plus_plain` match exactly, `shadows` has 31 isolated shadow-edge pixels over 8 (mean 0.02).
