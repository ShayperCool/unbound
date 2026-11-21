# Core Capabilities and Baselib Memory Pages

Unity 6000.0.71f1, recorded with the Linux GPU Editor in batch mode on both OpenGL Core
(`Reference/unity-6000.0.71f1/opengl.jsonl`, 97 records) and Vulkan
(`Reference/unity-6000.0.71f1/vulkan.jsonl`, 97 records); two identical independent captures per
device on an NVIDIA RTX 4060.

`Assets/Scripts/CoreCapsProbe.cs` runs in Play mode and measures:

- `bufferLimit.*`: `SystemInfo.maxGraphicsBufferSize` (2 GiB = `2147483648` on both OpenGL Core
  and Vulkan), creating a valid `GraphicsBuffer`, and creating one exceeding the maximum size
  (throws `ArgumentException` naming the requested and maximum byte counts).
- `tiled.*`: `SystemInfo.maxTiledPixelStorageSize` and `SystemInfo.GetTiledRenderTargetStorageSize`
  across 6 `GraphicsFormat` values (`R8G8B8A8_UNorm`, `R16G16B16A16_SFloat`, `D32_SFloat_S8_UInt`,
  `R8_UNorm`, `None`, `RGBA_DXT1_UNorm`) and 5 sample counts (`1`, `4`, `8`, `0`, `-1`).
- `dynres.*`: `SystemInfo.supportsDynamicResolution`, `Camera.allowDynamicResolution`,
  `ScalableBufferManager.ResizeBuffers` 20-step 0.05 quantization and validation assertions
  (including out-of-range, negative, zero, and `NaN` on both OpenGL Core and Vulkan), camera
  `pixelWidth`/`scaledPixelWidth`/`pixelRect` on dynamic, fixed, odd-dimension, and explicit
  (`useDynamicScaleExplicit` + `ApplyDynamicScale`) render textures, plus render/readback/blit
  controls at `(0.5, 0.25)` and `(0.5, 0.5)`.
- `baselib.*`: `Unity.Baselib.LowLevel.Binding` page APIs (`Baselib_Memory_GetPageSizeInfo`,
  `AllocatePages`, `AllocatePagesEx`, `ReleasePages`, `SetPageState`, `SetPageStateEx`): aligned
  allocations, `Reserved -> ReadWrite -> ReadOnly -> Reserved -> ReadWrite` round-trip (confirming
  re-committed pages after `Reserved` read zero), `NoAccess`, executable page states, `Ex` variants,
  error codes (`InvalidPageCount`, `InvalidPageSize`, `UnsupportedAlignment`,
  `UnsupportedPageState`, `OutOfMemory`, null-pointer and zero-count no-ops), and sticky
  `Baselib_ErrorState` preservation.

## Commands

```
python3 tests/projects/corecaps/run.py editor  --release 6000.0.71f1 --device opengl --label <label>
python3 tests/projects/corecaps/run.py publish --release 6000.0.71f1 --device opengl --label <label>
python3 tests/projects/corecaps/run.py unbound --release 6000.0.71f1 --device opengl
python3 tests/projects/corecaps/run.py unbound --release 6000.0.71f1 --device vulkan
```

## Known differences and unmeasured scope

- `residuals-opengl.json` (1 key): `Graphics.Blit(null, rt, mat, pass)` reading `_ScreenParams`
  sees zero in the Editor (also tracked in `tests/projects/nativegfx/residuals-metal.json`), whereas
  Unbound supplies the target dimensions.
- `residuals-vulkan.json` (8 keys): Unbound scales both render texture axes independently and
  follows the Metal dynamic-resolution readback model (`tests/projects/nativegfx`), whereas the
  Linux Vulkan Editor scales both surface dimensions by `widthScale`, retains the half-rendered
  image when only `heightScale` changes in the same frame, upscales full-size `ReadPixels` without
  an out-of-bounds error, and diverges on the two Blit shader parameter checks.
- Unmeasured: macOS Editor, non-NVIDIA GPUs, non-zero `extPageState` flags, and accessing protected
  pages (`SIGSEGV` fault tests).
