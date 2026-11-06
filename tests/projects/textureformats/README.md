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
- `AsyncGPUReadback` of a 4x MSAA target reads its last resolve: drawn into but not sampled since,
  it reads as zeros; once sampled, the drawn texels.
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

## Open in Unbound

- The MSAA readback values in `residuals.json`: Unbound resolves a multisampled target at the end
  of every pass, so its readback reads the drawn texels.
