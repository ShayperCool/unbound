# Command buffer probe

Unity 6000.0.71f1, built-in pipeline, Linear color space (`Assets/Scripts/CmdBuffersProbe.cs`,
fragment programs in `Assets/Resources/UavWrite*.shader`). Every case records its values, exceptions
(`.error`) and log messages (`.log`); GPU results are read back (`ComputeBuffer.GetData`,
`AsyncGPUReadback` as hex per layer).

* **Random write targets** (`uav.*`, `cmd.uav.*`): `Graphics.SetRandomWriteTarget` and
  `CommandBuffer.SetRandomWriteTarget` of compute and graphics buffers into a fragment program that
  writes a `RWStructuredBuffer` at `register(u1)` (and at no register), at other indices, with a
  material's or property block's buffer of the same name, cleared before the draw, kept over draws and
  across command buffers, replaced (also by a render texture); Append buffers' counters reset or
  preserved and counting over draws; argument errors (indices, null, released buffers, render textures
  with and without random writes).
* **ConvertTexture** (`convert.*`, `graphics.convert.*`): formats (half, float, R8, linear and sRGB,
  RGB24, BGRA32, compressed), an upscale, slices of arrays, cubemap faces and cubemap arrays, refused
  conversions (cubemap into 2D or array, render texture destinations, out-of-range elements, null and
  built-in targets) and `Graphics.ConvertTexture`'s answers.
* **Temporary arrays** (`tarray.*`): `GetTemporaryRTArray` descriptors for every overload, zero and
  one slice, replaced and released temporaries, slices cleared and blitted into, and sizes relative to
  a camera's (-1, -2, -3).
* **Instanced draws** (`cmd.instanced.*`): `CommandBuffer.DrawMeshInstanced` of a quad into an 8x2
  target (identity view and projection), every overload, per-instance and single block values,
  matrices and block changed after recording, 1023 and 1024 instances, raw instance IDs, ordinary uniform arrays alongside instanced arrays, and its argument errors.
* **Indexed procedural draws** (`cmd.indexed.*`, `cmd.indirect.indexed.*`, `graphics.*indexed.now`):
  a program that places vertex `v % 81` on an 8x8 grid draws cells picked by 16- and 32-bit index
  buffers, instanced, indirect with a start index and base vertex (graphics and compute argument
  buffers), through `Graphics.DrawProceduralNow`/`DrawProceduralIndirectNow` too, and the argument
  errors.
* **Compute parameters** (`cmd.cbuffer.*`, `handle.*`, `cmd.handle.*`): `SetComputeConstantBufferParam`
  of graphics and compute constant buffers, at the device's offset alignment, written after
  recording; `SetComputeBufferParam` of a `GraphicsBufferHandle`.
* **Graphics fences** (`fence.*`): support flags, async queue and CPU synchronisation fences from
  command buffers and `Graphics`, `passed` before and after they run, after the GPU finished and
  three frames later, waits and their argument errors.
* **Readbacks into slices** (`slice.*`, `cmd.slice.*`): `AsyncGPUReadback.RequestIntoNativeSlice` and
  `CommandBuffer.RequestAsyncReadbackIntoNativeSlice` of buffers (ranges), textures (regions, formats),
  too small, larger and strided slices.

## Recording

```
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/cmdbuffers/run.py native --label <fresh>
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/cmdbuffers/run.py publish <fresh>-1 <fresh>-2
UNBOUND_CLI=/path/to/unbound python3 tests/projects/cmdbuffers/run.py check
```

`native` records two batchmode Editors on the NVIDIA GPU; `publish` needs both captures complete and
byte-identical. `verify` compares every value exactly and exits nonzero on any difference. `check` runs that
same strict report, then checks the explicitly required safe Append skip separately: exactly the
`uav.append.nocounter` payload and its missing Editor warning may differ, the entire 48-word
buffer must retain its sentinel, and the engine log must contain the counter-owner warning.
All other missing, extra and failing values fail the gate. This is an intentional safety
difference for invalid Append bindings, not an insignificant visual residual.

## What the Editor does (OpenGLCore, RTX 4060)

* A random write target binds at the program's UAV register; a buffer without a register is at u1
  (after its one render target). It wins over a material's or draw's buffer of the same name, which
  binds again once the targets are cleared. A buffer target restarts its counter from 0 unless
  `preserveCounterValue` (then the count goes on from `SetCounterValue`'s); a render texture set at
  the index takes the buffer's place. Targets stay set until `ClearRandomWriteTargets`, also
  after a command buffer that set them. `Graphics` checks indices (0..7 accepted), null and released
  buffers and render textures without random writes (null textures are accepted) when called;
  `CommandBuffer` checks indices and null buffers when recorded (other exception types and messages)
  and logs a render texture without random writes when run.
* `ConvertTexture` converts colors as a blit does (sRGB decoded and encoded) and samples with the
  source's filter mode; it refuses a destination that cannot be rendered into on the device (BGRA32,
  compressed) but converts into RGB24. A cubemap source converts into a cubemap only, from its first
  face whatever `srcElement` says.
* `GetTemporaryRTArray` makes a `Tex2DArray` of at least one slice; -N sizes divide the camera's.
* Readbacks into a slice write contiguously from its first element, also into a strided slice.
* `CommandBuffer.DrawMeshInstanced` checks when recorded: a material without instancing throws
  (the Scripting API says it logs when run), null matrices throw a NullReferenceException, a count
  past the matrices' length throws, 1024 instances are drawn; an invalid pass logs when run. A
  block's single value of an instanced property reaches instance 0 only (the others keep the
  material's).
* Indexed procedural draws read 16- and 32-bit indices; `SV_VertexID` includes the indirect base
  vertex. A Structured buffer draws as an index buffer; a count past the buffer draws without an
  error.
* A constant buffer is read from its offset (alignment 256) and with what it holds when run.
* `supportsGraphicsFence` is true, `supportsAsyncCompute` false: `passed` of an async queue fence
  throws NotSupportedException. A CPU synchronisation fence answers false in the frame it runs,
  even after the GPU finished, true three frames later, never when not run. Waiting on a CPU fence
  or an unmade one throws.
* An Append program whose buffer has no counter warns and appends through some other counter
  (a GPU race); Unbound skips such a draw with a warning.

## Unity 6000.0 contracts and observations

| Source | Documented guarantee / limitation | Measured context |
|---|---|---|
| [DrawMeshInstanced](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DrawMeshInstanced.html), [GPU instancing](https://docs.unity3d.com/6000.0/Documentation/Manual/gpu-instancing-shader.html) | Explicit pass or all passes, per-instance property arrays; instancing support required | 6000.0.71f1 OpenGLCore accepts 1024 instances and throws at record time for disabled instancing despite the API page's execution-time description. A scalar instanced property changes only instance 0; ordinary scalar uniforms still apply to all. |
| [DrawProcedural](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DrawProcedural.html), [DrawProceduralIndirect](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DrawProceduralIndirect.html) | Indexed variant has no vertex buffer; indirect arguments contain index count, instance count, first index, base vertex and first instance | Readbacks cover 16/32-bit indices, base vertex and both argument buffer types. Topologies other than triangles, lines and points remain refused loudly. |
| [SetComputeConstantBufferParam](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetComputeConstantBufferParam.html), [SetComputeBufferParam](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetComputeBufferParam.html) | Constant binding has byte offset and size with device alignment; regular buffer bindings are per kernel and retain counter values | Constant offsets align to 256 bytes on this device; captures cover recording-time mutation and graphics/compute buffers. The renderer binds the GPU buffer directly, preserving preceding GPU writes. |
| [CreateGraphicsFence](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.CreateGraphicsFence.html), [WaitOnAsyncGraphicsFence](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.WaitOnAsyncGraphicsFence.html), [passed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.GraphicsFence-passed.html) | Fence follows preceding GPU work; waits affect the GPU and accept async queue fences only | Async compute is absent and async fence `passed` throws. CPU fences remain false during the recorded frame and true three frames later after completion. One-frame visibility is false, two-frame visibility is true in both additional captures. Unbound uses actual GPU queue completion and exposes it from two frames later; single-queue waits need no barrier. |

The two independent final Editor captures are byte-identical to the committed reference. The
reference retains the invalid Append case's unrelated-counter writes and warning. Unbound's
skip is required by the task and changes invalid-input behavior; it is not claimed as Unity parity.

## Indexed RenderParams contract, Unity 6000.0

`render.indexed.*` uses a camera render in each frame, not a CommandBuffer draw into a
standalone target. `RenderIndexedProbe.cs`, `IndexedGrid.shader`, `IndirectGrid.shader` and
`IndexedFill.compute` cover 16/32-bit indices, the documented Structured `sizeof(int)` buffer,
index offsets, commands and `startCommand`, vertex/instance IDs, and CPU/GPU buffer writes
between the API call and camera execution. The shader reads vertices above 65535. The indirect
shader uses the release's `UnityIndirect.cginc`, including its instance-count and base-instance
helpers. These cases do not establish any order between overlapping draws.

| Official 6000.0 source | Guarantee or limitation applied |
|---|---|
| [RenderPrimitivesIndexed](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderPrimitivesIndexed.html) | The shader generates or fetches vertices using the supplied indices. The example uses a Structured buffer with `sizeof(int)` stride. |
| [RenderPrimitivesIndexedIndirect](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderPrimitivesIndexedIndirect.html) | GPU arguments control indexed draws and instances, with command count and first command. Compute shader support is required. World bounds cull and sort the call as one entity. |
| [IndirectDrawIndexedArgs](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.IndirectDrawIndexedArgs.html) | Use the platform's struct and `size`, rather than assuming a portable layout of raw ints. Fields specify count, instances, first index, base vertex and first instance. |
| [GraphicsBuffer.Target](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.Target.html) | Targets depend on the device. DX11 cannot combine Index and Structured; OpenGLCore acceptance of Structured as indices is measured here, not promised on every backend. |
| [RenderMeshIndirect shader example](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html), shipped `Editor/Data/CGIncludes/UnityIndirect.cginc` | Define `UNITY_INDIRECT_DRAW_ARGS` and call `InitIndirectDrawArgs` before the command/vertex/instance helpers. The include implements backend-dependent instance IDs. |

The measured release is 6000.0.71f1, OpenGLCore on RTX 4060. Shader vertex inputs with 32-bit
indices or GPU base vertices remain refused by R6's renderer guard; procedural shaders bind no
vertex buffer. Topologies beyond triangles, lines and points and strides beyond 2/4 bytes remain
separate scope and fail loudly. Standalone mesh-instancing logical IDs and XR/plugin stubs are
separate tasks.

6000.0.71f1 observations for `render.indexed.*`: both final Native repeats are byte-identical.
Direct 16/32-bit and Structured int buffers draw the selected cells, including IDs over 65535.
Indirect start index and base vertex change the fetched vertex; `startCommand` also changes
`GetCommandID(0)` to the absolute command index. Raw instance IDs and `GetIndirectInstanceID`
start at zero on this GLCore device, while `GetIndirectInstanceID_Base` includes the argument's
start instance. CPU writes after queuing and GPU writes before camera execution are visible to
both the index fetch and shader argument reads. A direct instance count of zero renders one
instance on this device. Null index/command buffers throw ArgumentNullException; null material
draws nothing; an argument buffer missing IndirectArguments logs an error and draws nothing.
Unbound retains its existing explicit range checks for unsafe direct/command ranges. These
checks are defensive restrictions, not claimed as measured Unity exception contracts.
