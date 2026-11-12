# BatchRendererGroup probe

A BatchRendererGroup in the shader graph probe's URP scene (tests/projects/shadergraph, staged with
unsafe code allowed and the BatchRendererGroup variants kept; its Lit sphere stays as an ordinary
renderer), built as the Manual's "Creating a renderer with BatchRendererGroup" does: one batch in a
GraphicsBuffer of the device's `BatchRendererGroup.BufferTarget` (a raw buffer, or a constant
buffer given to AddBatch with its window), a zeroed block at address 0, packed float3x4
`unity_ObjectToWorld` / `unity_WorldToObject` and per-instance `_BaseColor` flagged `0x80000000`.
The culling callback emits four draw commands in one range: eight URP Lit cubes, two URP Unlit
spheres, and two mirrored cubes (negative x scale), one with `FlipWinding` and one without.

`Assets/Scripts/BrgProbe.cs` renders the main camera twice into a 320x180 texture and writes
`basic.png` and `values.jsonl`: the API's answers (buffer target, window size, the IDs of
registered meshes, materials and the batch) and every callback's context (view type, projection,
masks, planes, splits, LOD parameters, view ID).

    python3 tests/projects/brg/run.py native --label L [--device vulkan]   # two Editor captures
    python3 tests/projects/brg/run.py publish L-1 L-2 [--device vulkan]
    python3 tests/projects/brg/run.py check [--device vulkan]              # the land gate

The references (`Reference/unity-6000.0.71f1/opengl`, `.../vulkan`) are two identical captures each
of the Editor on its OpenGL Core and Vulkan devices, measured on the NVIDIA driver only. The
Editor draws the batch on both devices (the Manual's platform list names only Vulkan for Linux);
the two images are identical. Unbound matches both exactly (82/82 values, no pixel differs).
