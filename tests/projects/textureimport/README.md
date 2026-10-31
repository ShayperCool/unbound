# Texture import probe

What the TextureImporter gives a game, byte for byte: every texture's size, mip count, format and
the pixels of every mip level (`GetPixels32`, bottom row first). The 75 textures (`make_textures.py`
writes the sources; one PNG and one importer setting per case) cover box and Kaiser mipmaps in
sRGB and linear, fade out, border mipmaps, preserve coverage at three alpha-test values, alpha is
transparency, alpha from grayscale and alpha source None, non-power-of-two textures without
scaling (odd sizes, one odd side) and scaled to nearest/larger/smaller, the max size with the
Mitchell and Bilinear resize algorithms, and normal maps: plain, with Kaiser mips, flipped green
and fading, and made from grayscale with the standard and Sobel filters at eleven height scales,
three sizes, gray and coloured sources and impulses (filter kernels). Every case is readable and
stored as RGBA32 (platform overrides; uncompressed default platform), so compression hides
nothing.

Profiles: Gamma and Linear projects on the Standalone target, and a gamma project on the Android
target (`-buildTarget Android` / `--target Android`). Editors run on the GPU (private Xvfb,
VirtualGL) through the shared heavy-job launcher; every run works on a copy in the validation
cache.

    python3 tests/projects/textureimport/run.py editor --release <version> --label <L>    # two Editor runs per profile
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

The project's color space changes nothing: the Gamma and Linear references are identical.

* **Mip chain.** Every level is filtered from the level above kept in 32-bit float (sRGB colour
  decoded to linear light; alpha and linear textures as stored) and rounded to bytes on its own,
  so rounding does not accumulate. Box: each texel samples the level above bilinearly at its
  centre, a 2x2 average for even sides. Kaiser: a Kaiser-windowed sinc (width 2, alpha 4) over 8
  texels, mirrored at the edges, while both sides are even (or 1); a level with an odd side uses
  the box filter.
* **Fade out**: level i's encoded colour moves to mid-gray (127.5) by (i - start) / (end - start),
  clamped; alpha stays. Normal maps fade to the flat normal (0.5, 0.5, 1). The fade does not feed
  the next level.
* **Border mipmaps**: edge texels average two texels of the left or right column (the right one in
  a 1-wide level; corners are columns) or of the bottom or top row, whatever the filter or size.
* **Preserve coverage**: alpha is scaled up (never down) so that a level passes the alpha test
  about as often as the top level; the chain continues from the scaled level.
* **Normal maps**: on Standalone (DXT5nm encoding) a texel is stored as (1, y, y, x); on Android
  (XYZ, the target whose shaders define `UNITY_NO_DXT5nm`) as (x, y, z, alpha). Mips filter the
  colour without renormalizing. Flip green inverts y before the chain.
* **Create from grayscale**: height is the mean of red, green and blue. Normals use central
  differences (standard) or Sobel divided by 4, wrapping at the edges, with the slope scaled by
  `0.08 * (10^heightScale - 1) * (width + height) / 2`, so the bumpiness grows exponentially and
  with the texture's size. Every level filters the colour and makes its normals from its own height
  and size. With flip green the DXT5nm texel is (1, -y, y, x).
* **Alpha from grayscale**: the mean of the colour channels, rounded down. **Alpha source None**
  only makes an automatic format alpha-less: an explicit RGBA32 keeps the input alpha.
* **Resize** (NPOT scaling, max size with Mitchell): each axis apart, x first, with a
  Mitchell-Netravali filter (B = C = 1/3) widened by the scale when shrinking and a cubic B-spline
  when enlarging, edges clamped, on the stored bytes (no colour-space conversion or alpha
  weighting). The Bilinear algorithm interpolates the two texels around each texel centre, x then
  y, rounding bytes down after each pass.
* Alpha is transparency only changes the top level; mips filter the dilated texels as usual.

## What differs

* Alpha is transparency: the Editor fills each transparent texel with one visible texel's colour
  in a pattern not yet reproduced (not the nearest texel, not a fixed-order flood fill); Unbound
  averages the nearest visible neighbours. The cases' mip levels differ accordingly.
* Preserve coverage: the Editor searches the alpha-test threshold in a way not yet reproduced;
  a few dozen alpha bytes differ by 1, and with reference 0.8 one 2x2 level by up to 18.
* Rounding ties: 1-wide differences where an exact half meets the Editor's float evaluation
  order (odd-size bilinear levels, flipped green, Sobel and flat grayscale normals).

Runners select the Editor release explicitly and write it into disposable project copies.
Use a fresh label for each run; existing projects and captures are preserved. Only the
release documented above has been measured so far.
