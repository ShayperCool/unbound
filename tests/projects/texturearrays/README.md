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
  floor(height / R) texels; slice 0 is the top-left cell, then left to right, top to bottom. A
  Texture3D's depth slices follow the same order.
* The pixels do not depend on the project's colour space: Gamma and Linear differ only in the
  reported GraphicsFormat (`R8G8B8A8_SRGB` for sRGB textures in a linear project, UNorm in a
  gamma project) and in GPU samples of sRGB data. Android differs only in normal maps (XYZ).
