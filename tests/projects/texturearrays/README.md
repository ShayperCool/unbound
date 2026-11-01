# Texture array probe

What layered textures give a game, byte for byte: imported flipbooks of the TextureImporter
shapes Texture2DArray, Texture3D and Cube, and Texture3D, Texture2DArray and CubemapArray `.asset`
files. For every texture the probe records its type, size, depth, mip count, TextureFormat,
GraphicsFormat, dimension, readability and sampler state (filter, wrapU/V/W, aniso, mip bias), and
for every slice (array element, cube face) and mip level `GetPixels32` (RGBA bytes, bottom row
first), `GetPixels` (little-endian 32-bit floats) and `GetPixelData<byte>` (the stored bytes); a
failing call records its exception. It also draws slices through the GPU (`Graphics.Blit` with a
point-sampled array or volume shader at texel centres, read back with `ReadPixels`), including
depths outside the volume, which show the W wrap mode.

`make_textures.py` writes the PNG sources and `Assets/Editor/TextureCases.txt` (one importer case
per PNG: shape, flipbook columns and rows, colour space, mips and their filter, fade, border,
coverage, alpha is transparency, NPOT scaling, max size, normal map, filter and wrap modes,
readability). Every imported case is stored as RGBA32 (platform overrides, uncompressed), so
compression hides nothing. `ProbeRunner.Create` applies the cases and makes the `.asset` textures
(RGBA32 with and without mips, sRGB and linear, RGBAHalf, R8, unreadable) with deterministic
pixels, the sampling materials and the scene.

Profiles: Gamma and Linear projects on the Standalone target, and a gamma project on the Android
target. Editors run on the GPU (private Xvfb, VirtualGL) through the shared heavy-job launcher;
every run works on a copy in the validation cache.

    python3 tests/projects/texturearrays/make_textures.py      # after changing the cases
    python3 tests/projects/texturearrays/run.py create --release <version> --label <L>
    python3 tests/projects/texturearrays/run.py editor --release <version> --label <L>   # two Editor runs per profile
    python3 tests/projects/texturearrays/run.py publish --release <version> --label <L>  # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> python3 tests/projects/texturearrays/run.py unbound --release <version> --label <L>
    R=tests/projects/texturearrays/Reference/unity-6000.0.71f1 W=~/.cache/unbound/validation/runs/texturearrays/<L>
    python3 tests/projects/texturearrays/compare.py $R/Gamma.jsonl $W/unbound/Gamma.jsonl [--tolerance 1]

`compare.py` compares info lines and errors exactly and every byte of every pixel value within the
tolerance (default 0), listing each missing, extra or differing value.

## What the Editor does (6000.0.71f1)

Two independent captures per profile agree byte for byte (Renderer: NVIDIA GeForce RTX 4060).

* **Cells.** A flipbook of C columns and R rows is cut into cells of floor(width / C) x
  floor(height / R) texels (a 100-texel row in 3 columns gives 33-texel cells); slice 0 is the
  top-left cell, then left to right, top to bottom. A Texture3D's depth slices follow the same
  order (16 columns of a 256x16 LUT strip make a 16x16x16 volume).
* **Each slice is a 2D texture of the cell's size**: NPOT scaling and the max size apply per cell
  (20x20 cells become 16x16; 128x128 cells at max size 64 become 64x64), and an array slice gets
  the 2D mip chain with every mip setting (box/Kaiser, sRGB, fade, border, preserve coverage,
  normal maps), as tests/projects/textureimport describes it, with two exceptions:
  * **Alpha is transparency** fills only the first cell_w x cell_h texels of the whole flipbook
    image in bottom-up row order, read as one cell-sized image (the jump flood of
    textureimport); every texel after them keeps its colour. In a 128x64 two-column flipbook each
    64x64 slice is filled in its bottom 32 rows only.
  * **Border mipmaps**: the first halving takes its edge columns from the whole flipbook image,
    read from the slice's bottom-left texel on with the cell's width as the row stride (so a
    column steps by the cell's width, not the image's). The edge rows, and every lower level,
    read the slice itself.
* **Texture3D mips** do not use the 2D chain: each level is the one above halved along x, then y,
  then z, each pass the truncating mean (a + b) >> 1 of two bytes (an axis of size 1 is kept, an
  odd last texel dropped). sRGB and the mip filter change nothing (Kaiser equals box). The mip
  count follows the largest of width, height and depth (16x8x12: 5 levels).
* **Cube shape**: the faces of the strip; the sampler clamps on all three axes whatever the wrap
  settings (a Repeat meta reads Clamp, Clamp, Clamp). `Cubemap.GetPixels` gives a face top row
  first.
* **Asset files** store `m_Format` as a GraphicsFormat (RGBA32 is `R8G8B8A8_UNorm` with
  `m_ColorSpace: 1` for sRGB), the pixels in `_typelessdata`: an array's slices each with its mip
  chain in turn, a cube array's (cube, face) images likewise, a volume's levels one after
  another, each level all its depth slices (which halve, at least 1). `R8_UNorm` reads as
  TextureFormat R8.
* **CPU reads.** `GetPixels` gives a byte b as b * (1/255) in single precision (not b / 255,
  which differs in 42% of the values); R8 reads as (r, 1, 1, 1) and (r, 255, 255, 255).
  `GetPixelData` gives the stored bytes. An unreadable texture throws `ArgumentException:
  Texture2DArray.GetPixels32: texture data is either not readable, corrupted or does not exist.
  (Texture 'name')` (likewise `GetPixels` and Texture3D) and, from `GetPixelData`, the
  UnityException of a non-readable texture.
* The pixels do not depend on the project's colour space: Gamma and Linear differ only in the
  reported GraphicsFormat (`R8G8B8A8_SRGB` for sRGB textures in a linear project, UNorm in a
  gamma project, also for assets saved as sRGB) and in GPU samples of sRGB data. Android differs
  only in normal maps (XYZ).
* **Sampling.** A point-sampled slice at texel centres reads its texels; beyond the depth a
  Clamp wrapW gives the last (or first) slice and Repeat wraps. A 4x4x16 volume has 5 levels
  (level 4 is its 1x1x1 texel); a 4x4 array of 16 slices has 3, and a level past the last reads
  the last.

## What differs

Unbound matches every info line, every `GetPixels`/`GetPixels32` value and every stored byte in
Gamma, Linear and Android, and 14 of the 16 GPU samples (including every lower level of the deep
volume and array). Open:

* The two samples beyond the depth of a wrapW-Clamp volume: Unbound's renderer has no W wrap mode
  and repeats.
* `Cubemap.GetPixelData` (the imported cubes' 36 `raw` values) is not implemented and throws.
* The cases are 8-bit; float flipbooks, and border mipmaps of resized cells, are unmeasured.
