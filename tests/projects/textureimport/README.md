# Texture import probe

What the TextureImporter gives a game, byte for byte: every texture's size, mip count, format and
the pixels of every mip level (`GetPixels32`, bottom row first; a float texture's `GetPixels` as
little-endian 32-bit floats). The 78 textures (`make_textures.py` writes the sources; one PNG or
EXR and one importer setting per case) cover box and Kaiser mipmaps in
sRGB and linear, fade out, border mipmaps, preserve coverage at three alpha-test values, alpha is
transparency, alpha from grayscale and alpha source None, non-power-of-two textures without
scaling (odd sizes, one odd side) and scaled to nearest/larger/smaller, the max size with the
Mitchell and Bilinear resize algorithms (also soft 512x512 and 600x360 images and a float EXR
downscaled to 128 and 256 rows), and normal maps: plain, with Kaiser mips, flipped green
and fading, and made from grayscale with the standard and Sobel filters at eleven height scales,
three sizes, gray and coloured sources and impulses (filter kernels). Every case is readable and
stored as RGBA32, the float case as RGBAFloat (platform overrides; uncompressed default platform),
so compression hides nothing.

Profiles: Gamma and Linear projects on the Standalone target, and a gamma project on the Android
target (`-buildTarget Android` / `--target Android`). Editors run on the GPU (private Xvfb,
VirtualGL) through the shared heavy-job launcher; every run works on a copy in the validation
cache.

    python3 tests/projects/textureimport/run.py editor --release <version> --label <L>    # two Editor runs per profile
    python3 tests/projects/textureimport/run.py editor --release <version> --label <L> --job-worker-count N  # one more, N job workers
    python3 tests/projects/textureimport/run.py publish --release <version> --label <L>   # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> python3 tests/projects/textureimport/run.py unbound --release <version> --label <L>
    R=tests/projects/textureimport/Reference/unity-6000.0.71f1 W=~/.cache/unbound/validation/runs/textureimport/<L>
    python3 tests/projects/textureimport/compare.py $R/Gamma.jsonl $W/unbound/Gamma.jsonl [--tolerance 1]

`compare.py` compares sizes, mip counts and formats exactly and every byte within the tolerance
(default 0), and lists each differing mip with its largest difference. After changing the cases,
run `python3 make_textures.py`, then let the Editor apply the settings and remake the scene:
`Unity -batchmode -nographics -quit -projectPath tests/projects/textureimport -executeMethod
ProbeRunner.Create`.

## What the Editor does (6000.0.71f1)

The Gamma and Linear references agree for every 8-bit case. The HDR case's first level
differs with the project's color space, as described below. The arithmetic below
(mip halving, alpha dilation, coverage preservation, per-level processing, the
mip fade and resizing) reproduces the measured references, every step in 32-bit float in the
order given; Unbound matches every level of every profile byte for byte.

* **Mip chain.** The chain is made first, then every level is processed and rounded on its own
  (halves up), so rounding does not accumulate. A linear texture's top level stays in bytes: the
  first mip filters the bytes and scales by 1/255. An sRGB texture with mips is decoded to linear
  float first (a float table: `v = i * (1/255)`, `v / 12.92` or
  `powf((v + 0.055) / 1.055, 2.4)`) and encoded back per level in float
  (`powf(v, 1/2.4) * 1.055 - 0.055`).
* **Box**: a level with even sides is the 2x2 sum ((p00 + p10) + p01) + p11 times 1/4.
  **Kaiser**: a Kaiser-windowed sinc (width 2, alpha 4) over 8 texels around each pair, mirrored at
  the edges, the weights made in double, rounded to float and normalized by their float sum; x
  then y, each pass accumulating in float. A level with an odd side (1 included) is halved by the
  bilinear blit instead, whatever the filter: 16.16 fixed-point positions (step `(n << 16) / dn`,
  first `step / 2 - 0.5`), the next row and column clamped at the last, float interpolation.
* **Fade out**: `v * (1 - t) + target * t` on the gamma-encoded float level, t =
  (i - start) / (end - start) clamped to 0..1 (1 when start = end), towards mid-gray or the flat
  normal; DXT5nm fades its alpha (x) and keeps red. The fade does not feed the next level.
* **Border mipmaps**: the edge rows, then the edge columns (which win at corners), become the mean
  of texels 2i and 2i + 1 of the level above's edge, whatever the filter or size.
* **Preserve coverage**: when a level passes the alpha test (alpha above the reference) less often
  than the level above, the alpha threshold is bisected between 0 and the reference until the
  coverages agree within 0.5% (or the bounds within 0.5%), and alpha is scaled by
  reference / threshold, at most 1. A level that cannot reach the coverage keeps its alpha.
* **Alpha is transparency** (when the texture's format has alpha): a jump flood on the bottom-up
  image. Visible texels (alpha above 0) seed themselves; for steps k = min(max(w, h) / 2, 16),
  k / 2, ..., 1 each texel looks at its own candidate and those of the texels k left, right, below
  and above, keeping the first one nearest in Manhattan distance. Each transparent texel takes its
  candidate's colour with alpha 0; texels the flood never reaches keep their colour.
* **Normal maps**: on Standalone (DXT5nm encoding) a texel is stored as (1, y, y, x); on Android
  (XYZ, the target whose shaders define `UNITY_NO_DXT5nm`) as (x, y, z, alpha). Mips filter the
  colour without renormalizing; flip green inverts y of every level after filtering.
* **Create from grayscale**: the top level is converted to float (bytes times 1/255) and filtered
  down; every level's height is (r + g) + b times `(10^heightScale * 0.01 - 0.01) * (w + h) / 3`.
  Normals use 4 * (h(x+1) - h(x-1)) (standard) or the Sobel sum, wrapping at the edges,
  n = (-dx, -dy, 1) / sqrt(dy^2 + dx^2 + 1), stored as (1, y, y, x) or (x, y, z, 1) with each
  component times 0.5 plus 0.5. With flip green the DXT5nm texel is (1, -y, y, x).
* **Alpha from grayscale**: the mean of the colour channels, rounded down. **Alpha source None**
  only makes an automatic format alpha-less: an explicit RGBA32 keeps the input alpha.
* **Resize** (NPOT scaling, max size with Mitchell): stb_image_resize 0.97 on the bottom-up stored
  values (no colour-space conversion, no alpha weighting, edges clamped): Mitchell-Netravali
  (B = C = 1/3) on an axis that shrinks, the cubic B-spline otherwise; bytes decode as v / 255 and
  round in double. Float (HDR) images shrink with a variant
  `(27 m(x) + 2) / 35` instead (plain Mitchell misses 55k of the float case's 65,536 values), and
  a gamma project gamma-encodes HDR colour after the resize, not before. The Bilinear algorithm
  interpolates the two texels around each texel centre, x then y, rounding bytes down after each
  pass.
* **Job workers.** The Editor resizes the output rows in min(height / 16, workers) bands, each an
  stb region whose vertical mapping is recomputed in float, so bytes depend on the job worker
  count. Without `-job-worker-count` that is the online logical processors
  (`sysconf(_SC_NPROCESSORS_ONLN)`) less one, at least 1, at most 112: 15 on the measured
  16-thread machine. Captures with `-job-worker-count 1` and `3` (honoured: "JobSystem: Creating
  JobQueue using job-worker-count value N" in Editor.log) differ from the default in
  MaxSizeSoftWide (256x154: 3, 1 and 1 bytes of mips 0-2 at 1 worker; 1 and 1 byte of mips 0 and 2
  at 3), and Unbound's resize reproduces each capture only with that capture's count. Bands of
  equal power-of-two height (MaxSizeSoft, the float case) do not change.

## Measured coverage and limits

Gamma, Linear and Android each match all 502 mip levels and 1,732,492 pixel bytes
exactly, including the HDR case's format and float32 payload. Each complete recording
is byte-identical to its canonical reference and two independent Editor recordings.
Explicit RGBAFloat overrides retain float32 precision through the runtime descriptor;
default and explicit RGBAHalf imports retain their existing format. The importer test
`hdr_max_size_resize_matches_the_editor` also checks the HDR values bit for bit.

Runners select the Editor release explicitly and write it into disposable project copies.
Use a fresh label for each run; existing projects and captures are preserved. Only the
release documented above has been measured so far.
