# Render API members the pipelines use

Unity 6000.0.71f1, built-in pipeline, OpenGL Core (`Assets/Scripts/SrpMembersProbe.cs`, shaders in
`Assets/Resources`). Each record is a value, `ok`, or the exception's type and message.

* **Render target guard** (`guard.*`): `CommandBuffer.ThrowOnSetRenderTarget` (false by default)
  and every `SetRenderTarget` overload while it is set; `Blit`, `ClearRenderTarget` and
  `Graphics.SetRenderTarget` meanwhile.
* **GraphicsBuffer** (`create.*`, `usage.*`, `lock.*`): constructor checks, the usage flags the
  three-argument constructor gives each target, `LockBufferForWrite`/`UnlockBufferAfterWrite`
  checks and messages, the data after full and partial writes, an array used after its unlock,
  `GetData` while a lock is open (arrays filled with -1 or -2 beforehand), a lock of nothing,
  `SetData` afterwards.
* **Property block sub-elements** (`element.*`): a render texture with color and depth (cleared to
  0.25/0.5/0.75 and depth 0.3) and a depth-only one, bound through
  `MaterialPropertyBlock.SetTexture(id, rt, element)` and through the material for Default,
  Color and Depth, sampled by a full-target triangle into a float target; what `GetTexture`
  returns; a later plain texture; a null texture; the name overload.
* **Keywords** (`keyword.*`): `ShaderKeyword.name` and `IsValid` of a new global keyword, the
  default and an empty name, a shader's own keyword; a `ShaderKeywordSet` made with `new`.
* **Tiers and caps** (`tier.*`, `gles.*`, `tiled.*`): `Graphics.activeTier` (set and restored,
  also out of range), `Graphics.minOpenGLESVersion` (Standalone target),
  `SystemInfo.maxTiledPixelStorageSize` and `GetTiledRenderTargetStorageSize`.
* **DrawMeshInstanced** (`instanced.*`): red pixels of three cubes drawn through the eleven- and
  twelve-argument overloads into a camera's target, counts past the matrices, negative and zero
  counts, a proxy-volume usage without a volume (array and list overloads), null matrices.

```bash
python3 tests/projects/srpmembers/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/srpmembers/run.py publish --release 6000.0.71f1 --label <label>
python3 tests/projects/srpmembers/run.py unbound --release 6000.0.71f1
```

`editor` runs two independent windowed Editors on the GPU and compares them; `publish` writes
`Reference/unity-6000.0.71f1.jsonl` when they are identical (modelroot's comparator).

Measured (two identical captures, NVIDIA driver, OpenGL Core): every `SetRenderTarget` overload
but `(rt, mipLevel)` throws `Exception: Setrendertarget is not allowed in this context` while the
guard is set; a buffer that is only a vertex and/or index buffer is lockable by default; writes
past `countWritten` are kept; an unlock with a negative count releases the array but leaves the
lock open, and `GetData` leaves its array untouched while a lock is open; a depth sub-element
samples as (d, 0, 0, 1) through a property block as through a material, and an element the
texture lacks binds nothing and reads back as no texture; `new ShaderKeyword(name)` makes a valid
global keyword; a `ShaderKeywordSet` made with `new` enables nothing; tier 3; no OpenGL ES
version for the Standalone target; tile storage is the format's block size times the samples,
at most `int.MaxValue`; `DrawMeshInstanced` takes counts within the matrices only.

Not measured: other graphics devices (Vulkan, Metal) for the buffer reads during a lock, the
tier and tile storage; the Android target's `minOpenGLESVersion`; rendering with a
`LightProbeProxyVolume` through `DrawMeshInstanced`.

Contracts: [CommandBuffer.ThrowOnSetRenderTarget](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer-ThrowOnSetRenderTarget.html),
[GraphicsBuffer.LockBufferForWrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.LockBufferForWrite.html),
[MaterialPropertyBlock.SetTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.SetTexture.html),
[Graphics.DrawMeshInstanced](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.DrawMeshInstanced.html).
