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
| [GraphicsBuffer.Target.Structured](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GraphicsBuffer.Target.Structured.html) | Buffer stride must match the HLSL element, be a multiple of four and below 2048. Multiples of 16 are recommended for portability; mixed-size layouts can vary by graphics API. The 12-byte float3 case here is a GLCore observation from the documented drawing example. |
| [RenderMeshIndirect shader example](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html), shipped `Editor/Data/CGIncludes/UnityIndirect.cginc` | Define `UNITY_INDIRECT_DRAW_ARGS` and call `InitIndirectDrawArgs` before the command/vertex/instance helpers. The include implements backend-dependent instance IDs. |

The measured release is 6000.0.71f1, OpenGLCore on RTX 4060. Shader vertex inputs with 32-bit
indices or GPU base vertices remain refused by R6's renderer guard; procedural shaders bind no
vertex buffer. Topologies beyond triangles, lines and points and strides beyond 2/4 bytes remain
separate scope and fail loudly. XR/plugin stubs remain separate tasks. Standalone mesh instancing is measured below.

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

The documented float3 position buffer exposed a shader-layout defect: WGSL vec3 arrays
advanced 16 bytes through 12-byte elements. Runtime arrays of three 32-bit scalars now use
scalar struct elements, with vector loads/stores reconstructed by the compiler. Dynamic
component pointers into those packed elements fail explicitly; that case is not measured here.
The shader cache format changes with this translation.


## Standalone mesh instancing, Unity 6000.0

`graphics.meshinstances.*` runs `Graphics.DrawMeshInstanced` and
`Graphics.RenderMeshInstanced` through camera frame rendering and full RGBA readbacks.
`MeshInstanceProbe.cs` and `MeshInstanceIds.shader` derive geometry and color from both raw
`SV_InstanceID` and logical `unity_InstanceID`, including vertex-to-fragment transfer/setup.
The canonical release reference includes 38 new records from two byte-identical GPU Editor
captures. Every older record is unchanged. The engine-only `run.py check` gate compares all
276 records with the original exact comparator, retaining exactly the two required invalid
Append differences and its sentinel/warning guard.

| Official 6000.0 source | Applied contract or limitation |
|---|---|
| [Graphics.DrawMeshInstanced](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.DrawMeshInstanced.html) | Per-instance values use float/vector/matrix arrays on a MaterialPropertyBlock. Instancing must be supported and enabled on the material. At most 1023 instances per API call. Group culling/sorting remains unchanged. |
| [Graphics.RenderMeshInstanced](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.RenderMeshInstanced.html) | `startInstance` selects the first transform record; count -1 selects the remainder. Arrays, lists and NativeArrays are supported. Custom records use the mandatory `Matrix4x4 objectToWorld`; optional named `renderingLayerMask` and `prevObjectToWorld` members and arbitrary member order are documented. This probe covers only a first `objectToWorld` followed by ignored custom data. General struct offsets, rendering masks and motion vectors remain separate gaps. Capacity depends on per-instance data; the default transform limit is 511, and `assumeuniformscaling` can raise it. |
| [GPU instancing shader reference](https://docs.unity3d.com/6000.0/Documentation/Manual/gpu-instancing-shader.html) and [macro reference](https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/birp/gpu-instancing-shader/birp-shader-modifications) | `multi_compile_instancing` provides variants; SETUP initializes the logical ID, TRANSFER carries it to fragments, and ACCESS indexes per-instance data. `forcemaxcount` fixes the buffer array size on all platforms; `maxcount` is ignored on platforms with flexible sizes. These custom shader macros target the built-in pipeline. Multi-pass and additional-light instancing have documented limitations and are not newly measured here. |
| [SetFloatArray](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.SetFloatArray.html), [SetVectorArray](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.SetVectorArray.html), [SetMatrixArray](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MaterialPropertyBlock.SetMatrixArray.html) | A block retains the first array length. Longer updates truncate; shorter updates retain the old tail. Snapshot cases replace values at the same length, so they do not depend on resizing. |
| [Per-instance properties](https://docs.unity.com/en-us/engine/6000.0/manual/analysis/graphics-performance-profiling/birp/gpu-instancing-shader/per-instance-properties) | Missing instance properties use the material default, otherwise zero. The automatic GameObject batching warning about ordinary properties is not an explicit-Graphics-call contract. |
| [HLSL semantics](https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-semantics), shipped `UnityInstancing.cginc` | SV_InstanceID is a runtime-generated per-instance input. The release include computes the logical ID from raw input and base instance; backend details do not establish a portable raw-ID sequence. |

Measured on 6000.0.71f1 OpenGLCore, RTX 4060: IDs start at zero for each call and for the
selected RenderMeshInstanced range. Counts 2/3/7, separate groups, list/NativeArray/struct
inputs, start/count and count -1 agree. Property indices start at zero regardless of
`startInstance`. Ordinary scalar uniforms apply to all instances; instanced scalars affect
submission element zero only. Float/vector/matrix property arrays and ordinary arrays are
copied at submission, as are transform records. The snapshot cases mutate all of them before
camera execution.

Counts 513/1023 expose internal batch boundaries. The default shader restarts raw and logical
IDs after 511 instances, while property arrays continue at the submission index and an
instanced scalar is not repeated at a batch boundary. `forcemaxcount:4` restarts at four.
The uniform-scaling shader in this probe restarts at 682: its property struct contains a
vector, scalar and matrix. A 64 KiB property-buffer limit with a 96-byte struct explains that
observation; this size calculation is an implementation inference, not a documented universal
Unity allocation rule. Only the measured release receives these batch limits through Compat.
Unknown property types fail loudly during replay rather than guessing their batch capacity.
Other releases retain sequential IDs with an explicit unmeasured-limit warning.

The implementation reuses the existing logical first-instance dispatch and per-instance
property snapshots. A private shader replay variant keeps transform/property values per draw
and passes logical IDs through the standard instancing macros. Non-mesh procedural/indirect
calls and CommandBuffer instancing keep their existing paths. No new culling, sorting,
lighting or probe order is asserted. Other graphics backends/releases are not claimed by this
capture, and this result does not generalize the procedural-indirect raw-ID observations.


Additional Native recordings during this task also observed existing CPU-fence timing
variation: `fence.frame2.cpu.passed`, `fence.frames.cpu.executed` and
`fence.frames.cpu.immediate` were false in some complete repeats, and true in others.
All mesh-instancing records agreed. The prior fence oracle values and assertions remain
unchanged; raw complete captures and strict comparisons retain these differences outside Git.
This is a separate follow-up about Native fence progress in batchmode frames, not an
additional exception to the engine gate.

## Indirect argument pool coverage

`indirect.pool.grow.*` draws 257 independently observable pixels twice, with distinct
argument offsets, counts, empty draws and nonzero start instances. This crosses the initial
64-slot capacity and its next growth, then exercises reuse on the next submission.
`indirect.pool.writer.*` contrasts zero-count arguments with arguments written by a preceding
fragment draw in the same command buffer. The following indirect draw must see that write;
its argument copy cannot move ahead of the writing pass. Both the pixels and the caller's
argument words are recorded. Two independent Unity 6000.0.71f1 GPU Editor captures were
identical; all existing reference values are unchanged.

## Commands without XR or ray tracing devices

`device.*` records Unity 6000.0.71f1 on Linux OpenGL Core, with no XR device.
Record this independent section without the frame-timed fence cases, then append its new
keys to the existing canonical reference, preserving every older value:

```sh
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/cmdbuffers/run.py native --section devices --label devices
UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1 python3 tests/projects/cmdbuffers/run.py publish --section devices devices-1 devices-2
```

Both complete device captures are byte-identical. The ordinary engine gate runs all sections.

| Mechanism | Reference observation | Unbound |
| --- | --- | --- |
| Foveation configuration/mode, late-latch marks, occlusion mesh | No errors/logs or pixel changes without XR, including invalid mode values measured | Ignored |
| Late-latch projections | Null throws ArgumentNullException `projectionMat`; lengths other than two throw ArgumentException `Stereo Projection Matrix must be of size 2.` | Same checks, then ignored |
| Instance multiplier | Zero means one; multiplies direct instance counts, mesh instancing's too; persists between command buffers; indirect counts remain unchanged | Persistent render state, protocol 14 / NativeApi 34; mesh instances' added IDs NativeApi 65 |
| Stereo None/Instancing/Multiview | No pixel change in these no-device controls | Ignored |
| Stereo SideBySide | The following procedural draw produces no pixels, despite the documented VR limitation | Explicit NotSupportedException; no approximation of the unimplemented clipping state |
| Ray tracing, every command-buffer overload | Valid objects are accepted with no execution effect; required shader/compute/structure/indirect-args objects throw ArgumentNullException in the measured order; other null values are ignored | Same no-device checks and behavior |
| Ray tracing capabilities | supportsRayTracing, supportsRayTracingShaders and supportsInlineRayTracing are false; foveatedRenderingCaps is None | Same, without stub diagnostics |
| Acceleration structure construction | Creates an object and logs Assert `Platform doesn't implement RayTracingAccelerationStructure.` | Same, with nothing to release |
| Native plugin custom blit | Calls event 6 even without XR | The same call (tests/projects/nativegfx has what the plugin finds bound) |
| Native plugin texture updates, original/V1/V2 | Calls events 7/8 or 9/10 and updates GPU pixels; CPU pixels stay unchanged | The same events and pixels for `Texture2D` targets of 8-bit RGBA and RGB formats; other targets and formats throw NotSupportedException when recorded |

The two `.raytrace` resources also measure the standalone main-object local identifier for
distinct source names; the importer uses that identifier through Compat, and has no disk cache.
Shader settings and other ray tracing APIs outside these command-buffer members remain outside
this coverage. No ray tracing device or actual XR rendering behavior is inferred.

The strict comparator retains every difference. The gate additionally requires the precise
unsupported exception for SideBySide, with untouched pixels. Apart from the differences this
README names, it accepts no new one.
That is an explicit unsupported mechanism, not a parity claim. The plugin entry points run their
callbacks since native rendering plugins are supported, and their events compare exactly
(`device.plugin.*`; `docs/COMPATIBILITY_GAPS.md`, the native rendering plugins row). The follow-up
is no-device side-by-side clipping. Null plugin callbacks/targets
and null ray tracing constant buffers terminated both additional Editor repeats; they are omitted
from the in-process gate. Unbound reports a managed error for those unsupported/invalid calls.
Their repeat artifacts stay in the cache.

### Contract notes for Unity 6000.0

The following are documented; the table's exact errors and no-device results are observations:

- [SetInstanceMultiplier](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetInstanceMultiplier.html) persists, treats zero as one, and excludes indirect draws.
- [SetSinglePassStereo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetSinglePassStereo.html) is described as used only with VR enabled. The no-device SideBySide result above is kept separate from that statement.
- [SetFoveatedRenderingMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetFoveatedRenderingMode.html) and [ConfigureFoveatedRendering](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.ConfigureFoveatedRendering.html) configure supported foveated rendering.
- [MarkLateLatchMatrixShaderPropertyID](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.MarkLateLatchMatrixShaderPropertyID.html), [SetLateLatchProjectionMatrices](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetLateLatchProjectionMatrices.html) and [UnmarkLateLatchMatrix](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.UnmarkLateLatchMatrix.html) concern late-latched matrices. Marks are documented as ignored by the built-in pipeline and HDRP.
- [DrawOcclusionMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DrawOcclusionMesh.html) draws the active XR device's occlusion mesh.
- [BuildRayTracingAccelerationStructure](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.BuildRayTracingAccelerationStructure.html), [DispatchRays](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.DispatchRays.html) and the SetRayTracing members describe ray tracing commands; this capture has no ray tracing support.
- [IssuePluginCustomBlit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.IssuePluginCustomBlit.html) and [IssuePluginCustomTextureUpdateV2](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.IssuePluginCustomTextureUpdateV2.html) invoke native rendering callbacks. Their public plugin callback layouts are in the SDK's `IUnityRenderingExtensions.h`. They are not no-device no-ops.

The mesh multiplier control uses two distinct transforms and color elements, plus raw instance IDs; the placed control (`device.multiplier.placed.*`, `device.multiplier.camera.*`) places each instance by its raw ID instead of its matrix. A mesh-instancing call of n instances under multiplier m draws IDs 0 to n·m − 1, `unity_InstanceID` equal to the raw ID, through a command buffer and through a camera's queued `Graphics.RenderMeshInstanced` and `Graphics.DrawMeshInstanced` under the persistent multiplier. The added instances (IDs n and up) read the instance data past the call's elements: where no earlier draw of the section wrote there, zero matrices (they cover nothing) and zero instanced colors; after a call of four instances (`device.multiplier.stale.colors.2`, recorded last), the colors that call left. Unbound draws each call's instances one by one and then the added ones with zero matrices and instanced properties, warned of once; the gate requires the stale row to read zero past the call's own colors, and the strict report keeps its difference. Not measured: the added instances of standalone instancing past one native batch (warned, not drawn).

The two global acceleration-structure setters have the same recorded no-device ignore behavior with null and non-null structures. Their faithful annotations identify this measured behavior for the API audit, as for the other ignored device commands.
