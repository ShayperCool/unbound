# Readback Awaitable control

Status: prepared, not measured. No Awaitable reference or implementation is published.
Native admission and a matching Root runtime15 artifact are still required.

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

After Native admission is resolved:

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

After measurement and implementation, replay with a privately copied, complete
matching Root runtime15 artifact. The runner verifies every runtime-manifest hash,
requires CLI/Core/PhysX entries, and rejects the accepted frozen14 CLI. It never
builds a CLI or replaces a Core inside an existing artifact.

```sh
python3 tests/projects/gpubuffers/Awaitable/run.py unbound --release 6000.0.71f1 --label <fresh-label> --runtime <private-runtime15>
python3 tests/projects/gpubuffers/Awaitable/run.py compare --release 6000.0.71f1 --label <fresh-label>
```

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
multiple continuations. Its consumption behavior is Root-owned. The control records
second consumption without an intervening Awaitable allocation so that any shared
runtime discrepancy can be resolved there. `RequestIntoNativeArrayAsync` and all
NativeSlice paths are outside this control.

Missing validation: C# compilation, both Native captures and their repeat check,
one canonical Awaitable reference, measured failure and consumption semantics,
the ten implementations and their managed unit tests, and runtime15 replay.
