# Texture formats

Normalized 16-bit (R16, RG32, ARGB64/RGBA64), packed 10-10-10-2 (ARGB2101010) and integer
(R/RG/RGBA 8-, 16- and 32-bit SInt/UInt) render textures and textures, on the Editor's device
(Linux, OpenGL Core, NVIDIA). ARGB32 is the 8-bit control. `Assets/Scripts/TexFormatsProbe.cs`
records, per format, the exact bits of:

- `SystemInfo` answers: `IsFormatSupported` for every `GraphicsFormatUsage`, `GetCompatibleFormat`
  for Render and Sample, `SupportsRenderTextureFormat`/`Blending`/`RandomWrite`;
- 2x1 render textures (texel 0 gets A, texel 1 gets B): `GL.Clear`, a shader's solid and per-texel
  output (values out of [0, 1] included), straight-alpha blending, point and bilinear sampling,
  a kernel's writes (`enableRandomWrite`) and a 4x MSAA target, each read through
  `ReadPixels` into RGBAFloat (and once into RGBA64), `AsyncGPUReadback` in the target's own format
  and into RGBAFloat; integer targets written by integer shader outputs and kernels and read by
  `Texture2D<int4>`/`<uint4>.Load` (as bits);
- `Texture2DArray.Load` from a three-layer ARGBFloat array target with mips, at a layer and mip
  from material floats, by signed and unsigned coordinates (`rt.arrayLoad.*`);
- Texture2Ds: `SetPixel`/`GetPixel`, raw data, `SetPixelData`, GPU point and bilinear samples,
  `ReadPixels` and `AsyncGPUReadback`; creation in formats the device cannot sample.

ReadPixels from an integer target is a GL error on this device and leaves stale texels, so it is
not recorded.

## Measured (6000.0.71f1, both repeats byte-identical)

These are observations of this release and device, not documented guarantees:

- R16, RG32 and ARGB64 targets keep 16 bits through drawing, blending, sampling, kernels and
  AsyncGPUReadback; ARGB2101010 keeps 10/10/10/2 bits. Shader outputs clamp to [0, 1].
- `ReadPixels` reads 8-bit channels (b × (1 / 255) in a float texture) from every target that is
  neither a float format nor the texture's own format: R16/RG32/ARGB64/ARGB2101010 into RGBAFloat,
  R16/RG32 into RGBA64. ARGB64 into RGBA64 keeps 16 bits; float targets keep full precision.
- `AsyncGPUReadback` of R16/RG32 into RGBAFloat reads the missing channels as 1 ((r, 1, 1, 1),
  (r, g, 1, 1)); of A2B10G10R10 and every integer format it fails ("doesn't support ReadPixels
  usage"), as the format table says (no ReadPixels usage).
- A Texture2D in A2B10G10R10 or an integer format is refused ("Texture creation failed ... not
  supported for Sample usage"); the object is destroyed (`== null`). Texture2D RG32/RGBA64 keep
  16-bit texels: `GetPixel` reads (r, g, 1, 1) for RG32, `SetPixel` rounds half up.
- `AsyncGPUReadback` of a 4x MSAA target reads its retained resolve. Leaving its color
  attachment updates that image, including `RenderTexture.active = other` without a draw.
  `ResolveAntiAliasedSurface()` updates it while the source stays bound; resolving into a
  separate destination leaves the source's retained image unchanged. `ReadPixels` reads the
  current pixels, while compute `Texture2D.Load` reads the retained image. The history controls
  write different colors before every operation, so stale and current images are distinct.
- An allocated 2D MSAA target has one mip even with `useMipMap = true`. Its reported mip count
  survives release and changing the requested sample count. After release, changing its width, height, descriptor or
  mip settings recomputes it; recreation reports the new allocation. The 3D control retains three levels because volume targets are not
  physically multisampled. Descriptor `mipCount` remains the requested value.
- `GL.Clear` on an integer target stores the clear color's float bits, clamped to the channel's
  range (3.0 in R32_SInt reads 0x40400000, in R8_SInt 127).
- A draw whose material samples the target it draws into (`Graphics.Blit(rt, rt, ...)`, or another
  property bound to it) reads the target's contents from before the draw (`feedback.*`, ARGB32 and
  ARGBFloat, a full-target draw reading its own texel). Unbound samples a copy made before the
  draw.

## Run

From the repository root (the runner takes one launcher lease per Editor through
`tests/editor_runner.py`; do not wrap it in `with-heavy-job`):

```sh
python3 tests/projects/textureformats/run.py editor  --release 6000.0.71f1 --label native
python3 tests/projects/textureformats/run.py publish --release 6000.0.71f1 --label native
UNBOUND_CLI=... python3 tests/projects/textureformats/run.py verify --release 6000.0.71f1 --label check
```

`verify` runs the cases Unbound supports (`SUPPORTED` in `run.py`; `--cases all` runs every one)
and compares every value exactly with `compare.py`, which prints every missing, extra and failing
key. `residuals.json` lists the differences recorded as open: each still has to differ.
`TEXTURE_FORMATS_PROBE_ONLY=<prefix,...>` limits a run to cases (`rt.ARGB64`, `tex.RG32`, ...).

## Retained resolves and deferred differences

Q55 closes the eight earlier MSAA readback differences. The 6000.0.71f1 OpenGL Core
reference has 750 values, with every previous value preserved. The two
`rt.ARGB64.msaa.history.{targetBound,targetCleared}.asyncfloat` rows retain a one-ULP blue
component difference, Editor `0x3c808080` versus backend `0x3c808081`, about 1.86e-9.
These are Deferred; raw values remain in the strict report and `residuals.json`.

The [Unity 6000.0 ResolveAntiAliasedSurface API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.ResolveAntiAliasedSurface.html)
forces a resolve, into the source when no destination is supplied. Its source/destination
controls use different images. The [antiAliasing API](https://docs.unity3d.com/cn/6000.0/ScriptReference/RenderTexture-antiAliasing.html)
describes resolving when switching targets; the target-bound, target-cleared and unrelated-draw
controls distinguish the switch from a shader reading the source. The retained readback and
compute observations are device-specific measurements, not a documented ordering promise.
The [AsyncGPUReadback API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.html)
describes asynchronous transfer; captures complete their requests before examining the data.

Compat.RenderingMeasured selects retained resolves and allocated mip metadata for the measured
release. Unmeasured releases retain the earlier current-image readback and descriptor-only mip
metadata until recorded. Retained textures and views are created with their targets and retired
on replacement/release. Texture and buffer downloads share reusable staging storage, released
after an idle interval; returned CPU data stays owned by each request.

The `rt.msaaCopies.*` controls distinguish the multisampled attachment from its resolved
texture: `Graphics.CopyTexture` copies resolved texels without changing the attachment.
A forced resolve can therefore replace copied green texels with the destination's older red
samples, even without a new draw. An ordinary target switch publishes pending attachment
writes. A second source draw distinguishes copying its retained green image from its current
blue samples. The [Unity 6000.0 CopyTexture API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.CopyTexture.html)
requires matching sample counts; both copy targets use four samples and matching dimensions
and formats. These resolved-image details are measured on this OpenGL Core device.
The `rt.msaaLayers.*` controls retain green resolves while changing each of two array layers
to red. [ReadPixels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.ReadPixels.html)
reads the current render target in both layers; async readback still sees green.

Sampling and readback see the same copied resolve when the destination is unbound
(`sampleUnboundDestination`). A copy into a still-bound destination is replaced by its samples
on the next target switch, even if a prior explicit resolve was already completed
(`sampleBeforeReadPixels`, `afterFirstSample`). All ordinary shader bindings, including legacy
main-texture bindings, share the compute sampler's retained views.
The multi-layer array controls retain their resolved green images after the target switch and
ordinary sampling on this device. Explicit resolves publish both distinct blue/magenta layers.
The one-layer array control resolves automatically like a 2D target. These are measurements of
6000.0.71f1 OpenGL Core; the shared Compat rendering profile selects them. Array resolve views
are cached when the target is uploaded.

## Integer and signed 16-bit targets

Q58 adds 276 OpenGL Core records to the existing 763 without changing them, and a
separate 255-record Vulkan reference. Both references were captured twice on the
Unity 6000.0.71f1 Editor with NVIDIA RTX 4060. Other drivers are unmeasured.
Use `--device vulkan` with `editor`, `publish` and `verify` to select that device.
The Vulkan reference covers only the new format, viewport, readback and integer
feedback cases. Nine older packed UNorm async-to-float outputs did not repeat on
Vulkan, so that legacy set has no canonical Vulkan reference. Its raw captures
remain outside the repository; this does not establish parity for that set.

The [GL.Clear contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GL.Clear.html)
limits clearing to the active viewport and converts the requested depth for the
graphics API. `rt.partial.*` first writes different color and depth values over
the whole target, then clears only its right pixel through GL or CommandBuffer.
The float target is an independent depth control. Integer clears use the color's
raw float bits, clamped to each component's width. Packed UInt uses 10/10/10/2 bits.

The [GraphicsFormat descriptions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Experimental.Rendering.GraphicsFormat.html)
define signed normalized values in [-1,1] and require checking format usage.
R/RG/RGBA signed 16-bit targets retain their precision and negative values for
clears, drawing, sampling, blending and writable kernels. RGB is supported on the
measured OpenGL Core device and rejected by the Vulkan constructor. RGB uses an
opaque padding channel in Unbound; draws preserve it. Fresh-target drawing,
negative endpoints, values beyond the normalized range and storage-support
controls distinguish this behavior from an RGBA8 fallback.

ReadPixels conversion differs by device. OpenGL Core transfers nonfloat sources
into a different format through bytes. Signed normalized transfers use
`(2*n+1)/(2^bits-1)` before byte quantization. Vulkan retains source precision,
fills missing channels with 1 and leaves the destination unchanged when the
source does not support ReadPixels. R/RG float, unsigned normalized and signed
8-bit controls test the same rule beyond the new signed 16-bit targets. On the
measured OpenGL Core device, RGB16 signed async readback clamps negative values
to zero, while GPU sampling preserves them. `Compat.DeviceTextureReadback`
selects these measured conversions; other releases retain framebuffer conversion
and do not apply the RGB async clamp.

Two consecutive integer self-feedback draws read the image before each draw on
both measured devices. This is an observation, not a portable guarantee.
[Graphics.Blit](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.Blit.html)
leaves same-source/destination behavior undefined. Unbound retains its existing
feedback copy. Performance work on that copy remains on hold.

Descriptor construction rejects non-None color formats without Render usage on the
measured release. The signed RGB16 Vulkan failure and supported RG16 control
exercise that validation, as distinct from later GPU allocation.
