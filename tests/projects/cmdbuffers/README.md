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
byte-identical. `check` (the landing gate) runs Unbound and compares every value exactly.

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
