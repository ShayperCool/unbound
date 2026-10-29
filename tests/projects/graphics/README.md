# Graphics probe

Built-in render pipeline extension points, rendered into small render textures and read back with
`Texture2D.ReadPixels` by the same script in the Editor and in Unbound: camera clears, a renderer with
a property block, `Graphics.Blit` (copy, material, one pass, scale and offset), command buffers
(immediate, with a property block, setting a global, at a camera event), an `OnRenderImage` effect,
GL quads, `Graphics.DrawMeshNow`, queued `Graphics.DrawMesh`, a half-float target, `ReadPixels`
into an offset, how `RenderTexture.GetTemporary` reuses temporaries, compute shaders, meshes built
through the vertex/index buffer API and through MeshData, `DrawMeshInstanced` (with per-instance
colors), `RenderMesh`, `RenderMeshInstanced`, `DrawProcedural`, a Texture2DArray slice, a Texture3D
depth, a `CopyTexture` region, `SetRenderTarget`, `DrawTexture`, an `AsyncGPUReadback` and `GrabPass`
(unnamed, grabbed for each object; named, grabbed once per camera render);
several colors at once (render buffers, a command buffer's array), render buffers as a target, a
`CubemapArray`, procedural instances, indirect procedural and mesh draws (their arguments set from
the CPU and written by a compute kernel). Gamma
color space; the quality level's MSAA (2x) blends the half-covered pixels of camera renders.

Record Unity's reference:

    GRAPHICS_PROBE_OUT=$PWD/tests/projects/graphics/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/graphics \
        -executeMethod ProbeRunner.Record -logFile /tmp/graphics.log

Run Unbound and compare (every pixel within 2/255, half floats within 1e-3):

    GRAPHICS_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/graphics --no-watch --frames 30
    python3 tests/projects/graphics/compare.py tests/projects/graphics/Reference/unity-6000.0.71f1.json /tmp/unbound.json
