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
- Async readback of R16 into byte color layouts fills red, green and blue from
  the stored red channel and writes opaque alpha. The controls cover Texture2D
  and RenderTexture inputs, RGB and RG layouts, and destination channel order.
  Raw data, RG32 and RGBA64 inputs, and half-float output are comparison controls.
  Use `--device opengl-r16-byte` or `--device vulkan-r16-byte` to run the Linux
  references. Publishing checks both repeats for the requested graphics API,
  Unity release and complete payloads.
  [Unity 6.0 AsyncGPUReadback.Request](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.Request.html).
- Async readback of R16, RG32 and RGBA64 into single-precision floats multiplies
  each stored value by the rounded single-precision reciprocal of 65535 on Linux
  OpenGL Core and Vulkan. The dedicated `opengl-r16-float` and `vulkan-r16-float`
  profiles check R, RG and RGBA destination layouts from textures and render
  textures, including command-buffer completion and raw layout controls.
  [Unity 6.0 AsyncGPUReadback.Request](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.AsyncGPUReadback.Request.html).
- A Texture2D in A2B10G10R10 or an integer format is refused ("Texture creation failed ... not
  supported for Sample usage"); the object is destroyed (`== null`). Texture2D RG32/RGBA64 keep
  16-bit texels: `GetPixel` reads (r, g, 1, 1) for RG32, `SetPixel` rounds half up.
- `AsyncGPUReadback` of a 4x MSAA target reads its retained resolve. Leaving its color
  attachment updates that image, including `RenderTexture.active = other` without a draw.
  `ResolveAntiAliasedSurface()` updates it while the source stays bound; resolving into a
  separate destination leaves the source's retained image unchanged. `ReadPixels` reads the
  current pixels, while compute `Texture2D.Load` reads the retained image. The history controls
  write different colors before every operation, so stale and current images are distinct.
- The Unity 6.0 Scripting API specifies self-resolve when the command's target is
  omitted and requires matching dimensions and formats for an explicit target.
  On OpenGL Core in Unity 6000.0.71f1, this command resolves the selected source
  image into the destination's first image. The controls bind the source at either
  MRT color index, select a nonzero cube face, array layer and mip, and attach the
  command buffer to a camera. Shared immediate controls select the same source
  images. Same-format unequal extents copy the intersection without scaling.
  An omitted target publishes the retained color of an active ordinary MSAA
  source. Single-sample and bound-MSAA self-resolves leave their images alone.
  A supplied self-target checks the current attachment and leaves it alone.
  A created but inactive source that requires a resolve reports a native
  assertion. With the backbuffer active, an explicit single-sample destination
  receives its color, and an omitted destination publishes that color to the
  ordinary source's retained image. Screen-camera commands can see the
  camera's LDR or HDR intermediate before final presentation. The destination
  gets the opaque display color, clipped to both images. A built-in screen
  camera reads the display in its project's color space and uses the
  destination's format when writing it. A late `GL.sRGBWrite` change controls
  how its intermediate reaches the display.
  A partial camera presents its intermediate scaled to the presentation viewport
  while the rest comes from the display. The patterned controls measure bilinear
  interpolation across rows and columns when the viewport changes size. The viewport
  rounds its endpoints before computing the intermediate's size. A positive viewport that rounds
  to zero contributes no pixels. A zero-width or zero-height camera leaves
  the destination alone. Controls cover fractional and moved viewports,
  negative origins, normalized channels, half and full floats, and sRGB
  writes in Gamma and Linear projects. An explicit self-target keeps its
  earlier color. A different active render texture leaves the destination alone.
  Destroyed destinations act as omitted destinations. Released images skip the
  command, and a source recreated before execution supplies its new pixels.
  A depth-only source leaves the destination color unchanged.
  [CommandBuffer.ResolveAntiAliasedSurface](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.ResolveAntiAliasedSurface.html).
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
key. The Linux OpenGL Core profile expects an exact match. The shared comparison
on macOS still checks the differences recorded in `residuals.json`.
`TEXTURE_FORMATS_PROBE_ONLY=<prefix,...>` limits a run to cases (`rt.ARGB64`, `tex.RG32`, ...).

On Linux, use `--device opengl-resolve` or `--device opengl-resolve-linear`
to record, publish or verify the Gamma or Linear OpenGL Core command-buffer
resolve controls. Both profiles have their own files under
`Reference/unity-6000.0.71f1/`. The Linear profile changes the color space
in its private project copy.

## Retained resolves

The 6000.0.71f1 OpenGL Core reference preserves the earlier MSAA readback
observations. Both `rt.ARGB64.msaa.history.{targetBound,targetCleared}.asyncfloat`
rows now match the Editor's blue value of `0x3c808080` on Linux. The shared
macOS comparison still records its one-ULP difference in `residuals.json`.

The [Unity 6000.0 ResolveAntiAliasedSurface API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture.ResolveAntiAliasedSurface.html)
forces a resolve, into the source when no destination is supplied. Its source/destination
controls use different images. In the Linux OpenGL Core captures, a direct resolve
from an uncreated source preserves the destination and leaves the source uncreated. The [antiAliasing API](https://docs.unity3d.com/cn/6000.0/ScriptReference/RenderTexture-antiAliasing.html)
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

Q58 adds 281 OpenGL Core records to the existing 763 without changing them, and a
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

`rt.padding.*` keeps opaque RGB alpha separate from an RGBA MRT attachment's
quarter alpha and draws a renderer with GrabPass into the signed RGB target.
Graphics and CommandBuffer blits execute the shader passes without capturing a
GrabPass on this measured release; the renderer control captures the framebuffer.
These controls preserve different source colors so a skipped or stale grab is
visible. The shared rendering compatibility flag selects blit pass selection;
other releases retain the earlier selection until recorded.

Use `publish --append` when adding controls to this existing reference. The runner
checks every earlier raw record before appending, and refuses any changed or
missing record; both new Editor captures must also agree.

## Reading targets back on Metal

`--device metal` records the macOS Editor in batch mode (it draws with Metal) through
`tests/macos_editor.py`; `Reference/unity-6000.0.71f1/metal.jsonl` holds the `readback.*` cases,
two byte-identical captures on an Apple M2 Pro (macOS arm64). On macOS the engine draws with
Metal: the opengl check compares it with the Linux reference, and the vulkan check (macOS has no
Vulkan device) checks the Metal reference. The readback cases run only when named
(`TEXTURE_FORMATS_PROBE_ONLY`), so the other references stay as recorded. Each case clears a 2x1
target of one format to (0.25, 0.5, 0.75, 0.625) and reads it with ReadPixels into RGBAFloat and
RGBA32 and with AsyncGPUReadback (its own format and RGBAFloat), with the errors each logs.

- ReadPixels reads targets whose formats answer no ReadPixels usage where it converts their
  texels: B8G8R8A8 (Metal's LDR and Default format, UNorm, sRGB and SNorm), B4G4R4A4,
  R4G4B4A4, R5G6B5, E5B9G9R9, R10G10B10_XR and A2B10G10R10 (into RGBA32 with 2-bit alpha; into
  RGBAFloat an image blit conversion error). It refuses A1R5G5B5, R5G5B5A1, B5G5R5A1,
  A2R10G10B10 and A10R10G10B10_XR, asserting twice that the format is not supported for image
  operations (B5G5R5A1 with R5G5B5A1's id); the texels stay.
- AsyncGPUReadback reads a target whose format answers GetPixels usage and refuses one with
  neither ReadPixels nor GetPixels usage, naming the format (`A1RGB5 UNorm (72)`, ...). An
  RGBAFloat request of a target without ReadPixels usage logs that it failed but returns the
  converted texels; for A2B10G10R10 they are memory the Editor never wrote (the repeats
  differ), so only that the request succeeds is recorded.
- One- and two-channel targets read their missing channels as 1, as on Vulkan; B10G11R11 into
  RGBA32 reads alpha 0.

Unbound refuses, with its own message, a target format the rule does not name (unmeasured).
A request in a packed target's own format reads one packed word per texel (`readback.*.async`):
R4G4B4A4 as R4G4B4A4 and B4G4R4A4 as A4R4G4B4 from the highest bits down, B5G6R5 as R5G6B5,
A2B10G10R10 as itself, B10G11R11 as 10- and 11-bit unsigned floats with R lowest, E5B9G9R9 as
three 9-bit mantissas and a shared 5-bit exponent with R lowest, R10G10B10_XR as two set bits over
the 10-bit codes R, G, B (0xe009fefe: 512, 639, 766, each round(v * 510 + 384) with ties to even);
Unbound packs them from what it stores (`TargetReadbackRule.PackedWords`, the Metal profile's; the
other devices' are unmeasured), an XR target as half floats so every code is kept.
Reading a 4444 or 5-6-5 target returns the format's levels (0.25 reads 4/15 and 8/31), an XR
target each code over 1023 at 8 bits (512 reads 128/255), from Unbound's store
(`RenderTexture.QuantizeReadTexels`); a B8G8R8A8_SNorm target keeps signed channels (0.25 reads
32/127), and ReadPixels into RGBA32 truncates them (32/127 reads 64/255, 64/127 128/255).
`residuals-metal.json` lists the one remaining difference, B10G11R11's alpha into RGBA32 (Unbound
184/185 on Metal). Shaders sampling a 4444 target read 8-bit values on the Linux devices; a 5-6-5 or 1-5-5-5 target
keeps its channels' bits there (tests/projects/samplerstates Packed.*, GraphicsDeviceProfile
PackedTargetsKeepChannelBits); unmeasured on Metal, where Unbound's 8-bit store is sampled.
Whether the Vulkan Editor's ReadPixels reads a B8G8R8A8
target is unmeasured; Unbound keeps refusing every target without ReadPixels usage there.
