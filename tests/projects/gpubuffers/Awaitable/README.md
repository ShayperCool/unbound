# Readback Awaitable control

Status: measured for Unity 6000.0.71f1. Two independent Native processes exited
successfully with 225 byte-identical values across 14 cases. Their complete timing
sequences are recorded in `../Reference/unity-6000.0.71f1/awaitable/`. No Awaitable
implementation is published; shared consumption behavior and a matching runtime
artifact remain required.

The control pairs each of the ten `RequestAsync` overloads with `Request`. It uses
four buffer words and two RGBA32 pixels. It records request-time exceptions,
whether the returned Awaitable is already complete, await-time exceptions or an
error request, completion frame and thread relative to the callback, data access
twice on completion, data after expiration, and a second consumption of the same
Awaitable. It does not assume that `hasError` throws. Null, released, out-of-range
buffer and invalid-mip cases record both admission and completion outcomes.

The original source, 389-value reference and comparator stay unchanged. This
runner overlays the small script onto disposable copies of the existing project
and reuses its windowed runner. Two sequential independent Native processes must
finish successfully before publication. Their value files must match exactly.
Every timing sequence is retained in the one canonical reference under
`../Reference/unity-<release>/awaitable/`. Completion remains within 1–2 frames;
immediate events must remain immediate. Comparisons retain every missing, extra
and failing value, with the parent comparator's argument-message normalization.

Prepare copies without launching any heavy process:

```sh
python3 tests/projects/gpubuffers/Awaitable/run.py prepare --release 6000.0.71f1 --label <fresh-label>
```

To capture and publish a new release after shared admission:

```sh
python3 tests/projects/gpubuffers/Awaitable/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/gpubuffers/Awaitable/run.py publish --release 6000.0.71f1 --label <fresh-label>
```

The runner wraps each heavy process in the installed `with-heavy-job` once. Do
not wrap the runner again. Captures, logs, repeat comparisons, private project
copies and failed runs stay under the validation cache. Publication refuses to
overwrite a reference. Synthetic offline runner checks require no CLI or Unity:

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/projects/gpubuffers/Awaitable -p test_run.py -v
```

After implementation, replay with a privately copied, complete
matching Root runtime15 artifact. The runner verifies every runtime-manifest hash,
requires CLI/Core/PhysX entries, and rejects the accepted frozen14 CLI. It never
builds a CLI or replaces a Core inside an existing artifact.

```sh
python3 tests/projects/gpubuffers/Awaitable/run.py unbound --release 6000.0.71f1 --label <fresh-label> --runtime <private-runtime15>
python3 tests/projects/gpubuffers/Awaitable/run.py compare --release 6000.0.71f1 --label <fresh-label>
```

## Measured contract

All ten valid overloads return an incomplete Awaitable. The callback and await
continuation run on the main thread, in that order, one or two frames after the
requests. Await returns a successful request. Full buffer data is
`040302014433221188776655ddccbbaa`; the requested buffer range is
`4433221188776655`. Full texture data is `0102030405060708`; the requested region
is `05060708`. Each result permits both immediate data reads. Two frames later
both callback and awaited requests report done with an error and reject data
access with `InvalidOperationException: Cannot access the data as it is not available`.

An out-of-range buffer request and an invalid mip log errors and complete
immediately. Their Awaitables are already complete when returned; awaiting them
returns a request with `done` and `hasError` both true. It does not throw. Null and
released buffers throw `ArgumentNullException` for `buffer` from both request
methods before returning an Awaitable.

All twelve returned Awaitables reject a second `GetResult()` with
`NullReferenceException: Object reference not set to an instance of an object`.
The probe performs that read immediately after the first consumption, without
allocating another Awaitable. This exception is an observed result for this
release; it is not a promise about other uses of pooled Awaitables.

## Root handoff

Now-implemented stubs: **none**. Do not regenerate CoreModule for this checkpoint.
The exact ten pending signatures in generated CoreModule all return
`Awaitable<AsyncGPUReadbackRequest>`:

```csharp
RequestAsync(ComputeBuffer src)
RequestAsync(ComputeBuffer src, int size, int offset)
RequestAsync(GraphicsBuffer src)
RequestAsync(GraphicsBuffer src, int size, int offset)
RequestAsync(Texture src, int mipIndex = 0)
RequestAsync(Texture src, int mipIndex, TextureFormat dstFormat)
RequestAsync(Texture src, int mipIndex, GraphicsFormat dstFormat)
RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth)
RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, TextureFormat dstFormat)
RequestAsync(Texture src, int mipIndex, int x, int width, int y, int height, int z, int depth, GraphicsFormat dstFormat)
```

Each has a current callback-based `Request` counterpart. Once the control supplies
the missing evidence, the implementation belongs in the new
`managed/UnityEngine/Graphics/AsyncReadback.Awaitable.cs` partial and should reuse
`AwaitableCompletionSource<AsyncGPUReadbackRequest>` plus those callbacks.

The shared `Runtime/Awaitable.cs` currently allows repeated `GetResult()` calls and
multiple continuations. Its consumption behavior is Root-owned. The measured
second-consumption exception requires a shared runtime fix before the wrappers
can meet the complete contract. The wrappers must use the existing
`Awaitable<T>` and `AwaitableCompletionSource<T>` and the current request callbacks. `RequestIntoNativeArrayAsync` and all
NativeSlice paths are outside this control.

Remaining work: the shared consumption fix, the ten wrapper implementations and
their managed unit tests, CoreModule stub regeneration by Root, and replay using
a complete matching runtime artifact. The 15 offline runner tests passed at the
prepared checkpoint; the Native pair also compiled the focused C# probe.
