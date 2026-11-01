# GPU buffer and readback probe

Unity 6000.0.71f1, built-in pipeline, Linear color space (`Assets/Scripts/GpuBuffersProbe.cs`,
kernels in `Assets/Resources/BufferProbe.compute`). Every case records its values, exceptions
(`.error`) and log messages (`.log`).

* **Append, Consume, Counter** (`append.*`, `consume.*`, `counter.*`): Append and Counter
  `ComputeBuffer`/`GraphicsBuffer`s filled by kernels (`Append`, `Consume`, `IncrementCounter`,
  `DecrementCounter`, an 8-byte element), the hidden counter through `CopyCount` into a buffer of
  sentinels (offsets, alignment, past the end, Raw and Default destinations), `SetCounterValue`
  before and after dispatches, Default buffers. What kernels append is recorded sorted.
* **Indirect dispatch** (`indirect.*`): `ComputeShader.DispatchIndirect` and
  `CommandBuffer.DispatchCompute` with arguments from the CPU, from `CopyCount` and from a kernel,
  at offsets, in Default buffers, misaligned, past the end, zero groups, changed after recording.
* **Copies** (`copy.*`): `Graphics.CopyBuffer` and `CommandBuffer.CopyBuffer` between targets,
  sizes, strides and flags, null and released buffers, a command run after its source changed.
* **Buffer data** (`cmddata.*`, `setdata.*`, `getdata.*`): `CommandBuffer.SetBufferData` (array,
  range, `NativeArray`, `List`, other element sizes, ranges past the end) and the counter commands;
  `SetData`/`GetData` overloads and their errors.
* **Readbacks** (`rb.*`): `AsyncGPUReadback.Request`/`RequestIntoNativeArray` and
  `CommandBuffer.RequestAsyncReadback*` of buffers and textures (Texture2D mips and regions,
  formats, Texture2DArray layers, Texture3D slices, render textures, arrays and float targets):
  shapes, data as hex per layer, errors, `WaitForCompletion`, `WaitAllRequests`, `Update`, data
  frames after completion and writes after the request. Each request's events are
  `<phase><frame>:<done><error>` (r: right after the request, u: polled in `Update`, c: in its
  callback, w: after a wait); `values.jsonl` tells frame 0 from later ones (`n`), `timing.jsonl`
  keeps the exact frames.

## Recording

```
python3 tests/projects/gpubuffers/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/gpubuffers/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/gpubuffers/run.py unbound --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/gpubuffers/compare.py tests/projects/gpubuffers/Reference/unity-6000.0.71f1 \
    ~/.cache/unbound/validation/runs/gpubuffers/<label>/unbound --report <cache>/report.json
```

`publish` requires two windowed Editor captures on the NVIDIA GPU with byte-identical
`values.jsonl`, and writes every timing sequence they observed.

## What the Editor does (OpenGLCore, RTX 4060)

* **Hidden counters** start at 0. A buffer's counter comes into use with the first dispatch that
  binds it through a counter (Append, Consume, `IncrementCounter`): until then `CopyCount` writes
  nothing, also after `SetCounterValue` (whose value the first dispatch starts from), after binding
  it without dispatching, and after dispatching it as a plain RW buffer. Default buffers have no
  counter: `CopyCount` and `SetCounterValue` do nothing, silently. `SetData` leaves the counter.
  Counters keep counting across dispatches; `Consume` returns the element below the counter
  (16: elements 15 down to 8); `IncrementCounter` returns the value before, `DecrementCounter`
  the value after. `CopyCount` at an offset that is not a multiple of 4 warns
  `dstOffsetBytes must be a multiple of 4` and still writes the four bytes there; past the end it
  asserts `Assertion failed on expression: 'dstOffset + size <= m_size'` and writes nothing. Any
  buffer type takes the count.
* **Indirect dispatch** reads three `uint`s from any buffer (Default ones too) when the dispatch
  runs; a misaligned offset or arguments past the end dispatch nothing, silently; a null buffer
  throws `ArgumentNullException` (`argsBuffer`).
* **Copies** check, in order: null buffers (`source`, `dest`), released buffers
  (NullReferenceException), equal sizes in bytes (strides may differ), `CopySource` on the source
  and `CopyDestination` on the destination; `CommandBuffer.CopyBuffer` checks when recorded and
  copies what the source holds when it runs.
* **Buffer data**: offsets count in the data's element size (bytes of a `byte[]`, 8 of a
  `ulong[]`). `CommandBuffer.SetBufferData` copies the data and checks it when recorded (later
  changes of the array, list or native array do not reach the buffer); its range errors name
  `graphicsBufferStartIndex` and its overflow `GraphicsBuffer.SetData()`. `GetData` requires an
  offset that is a multiple of the stride.
* **Readbacks** read what the source holds when requested. A request completes one or two frames
  later (the two captures disagree on which, request by request): its callback runs in
  `EarlyUpdate.UpdateAsyncReadbackManager`, and `Update` of that frame sees it done. Its data can
  be read in that frame; one frame later `GetData` throws `Cannot access the data as it is not
  available` and the request reads done with an error. A failing request (out of bounds, invalid
  mip, a native array too small) logs, and calls back done with an error before `Request`
  returns; a null or released buffer throws `ArgumentNullException` (`buffer`).
  `WaitForCompletion`, `WaitAllRequests` and `CommandBuffer.WaitAllAsyncReadbackRequests` call
  back before they return; `AsyncGPUReadbackRequest.Update` completes nothing. A native array a
  request fills cannot be accessed while it is pending (`... can no longer be accessed, since its
  owner has been invalidated`); it receives the data on completion, a larger one keeps its tail.
  A texture request without a region reads every layer of the level (array slices, volume depth
  slices); conversions take bytes as b / 255 and floats to bytes rounded.

`compare.py` compares every value exactly. Mono's argument exception messages are compared in
CoreCLR's wording, as tests/projects/jobs does. A readback's timing passes when its events are
those of an Editor sequence and its later events fall within the frames the Editor showed for
that phase over all requests (1 to 2).
