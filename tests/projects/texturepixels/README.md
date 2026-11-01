# Texture2D pixel read probe

How `Texture2D` turns its stored bytes into floats. The probe makes 16x16 textures of RGBA32,
ARGB32, BGRA32, RGB24, Alpha8, R8, RG16, R16, RGBA4444, ARGB4444 and RGB565 (sRGB), RGBA32
linear, RGBA32 and R8 with mips (every level set explicitly, nothing generated), and an RGBA32
texture read back with `LoadImage` from its own PNG. Each is filled with `SetPixelData` so that
within a level every byte position takes every value 0..255. Per mip level it records
`GetPixels32`, `GetPixels(mip)`, `GetPixel` and `GetPixelBilinear` at every texel centre, the
block overload `GetPixels(1, 1, w - 2, h - 2[, mip])`, `GetPixelData<byte>`, and `GetPixels()`.

It then records:

* script-made 16x16x2 Texture3D volumes (RGBA32, BGRA32, R8 filled like the 2D textures; RHalf
  and RGFloat with set colours): `GetPixels32`, `GetPixels`, `GetPixelData` and `GetPixel` of
  every texel, and the bytes `SetPixel` stores for values half way between two bytes;
* the probe's own assets in `Assets/Pixels/Resources`: BGRA32 Texture2DArray and Texture3D
  `.asset` files, sRGB and linear (stored as GraphicsFormat B8G8R8A8_UNorm with
  `m_ColorSpace`), what `GraphicsFormatUtility.GetTextureFormat` returns for B8G8R8A8, and an
  uncompressed readable imported PNG encoded with `EncodeToEXR` before any other CPU read;
* the fixture's imported readable cubemaps (CubeStrip, CubeStripLinear): every face drawn by the
  GPU (`Assets/Pixels/Shaders/SampleCube.shader`, point by point at the face's texel centres)
  before any CPU access, after `Apply(false)` as the first access, and after `GetPixels` then
  `Apply(false)`;
* one script-made 4x4x4 Texture3D, bound to the volume sampling material throughout, drawn at
  depths 5 and -2 (beyond its 4 slices) while its `wrapModeW` goes Repeat, Clamp, Repeat, then
  Clamp with an `Apply(false)` and Repeat again.

`run.py create` has the Editor make those assets (`Assets/Pixels/Editor/PixelsAssets.cs`); the
other files of `Assets/Pixels` are copied into every capture's project.

There is no scene of its own: `run.py` copies the texture array fixture
(tests/projects/texturearrays) into the validation cache and replaces its probe script with
`Assets/Scripts/TexturePixelsProbe.cs` (same class and GUID). Profiles: Gamma and Linear,
Standalone target. The output has the texture array probe's format, so its comparator applies.

    python3 tests/projects/texturepixels/run.py create --release <version> --label <L>    # after changing PixelsAssets.cs
    python3 tests/projects/texturepixels/run.py editor --release <version> --label <L>    # two Editor runs per profile
    python3 tests/projects/texturepixels/run.py publish --release <version> --label <L>   # checks they agree, writes Reference/
    UNBOUND_CLI=<unbound> UNBOUND_MANAGED_DIR=<managed> python3 tests/projects/texturepixels/run.py unbound --release <version> --label <L>
    R=tests/projects/texturepixels/Reference/unity-6000.0.71f1 W=~/.cache/unbound/validation/runs/texturepixels/<L>
    python3 tests/projects/texturearrays/compare.py $R/Gamma.jsonl $W/unbound/Gamma.jsonl
    python3 tests/projects/texturepixels/analyze.py $R/Gamma.jsonl    # div / mul / other per value

`analyze.py` classifies every float against the stored byte b of the same texel: b / 255 or
b * (1 / 255) in single precision (126 of the 256 byte values tell them apart).

## What the Editor does (6000.0.71f1)

Two independent captures per profile agree byte for byte (Renderer: NVIDIA GeForce RTX 4060);
Gamma and Linear read the same floats.

* `GetPixels` (every overload and mip level) reads a stored byte b as b * (1 / 255) in single
  precision for RGBA32, ARGB32, BGRA32, RGB24, R8, RG16, an sRGB or linear texture, set
  textures and `LoadImage` results alike: 51 gives 0.200000018, not 0.2.
* `GetPixel` reads the same byte as b / 255 (0.2), and so does Alpha8's `GetPixels`, which
  returns (1, 1, 1, a) while its `GetPixels32` returns (0, 0, 0, a).
* R8 reads (r, 1, 1, 1) and `GetPixels32` (r, 255, 255, 255); RG16 (r, g, 1, 1).
* RGBA4444/ARGB4444 read each nibble n as (17 n) * (1 / 255) in both `GetPixels` and `GetPixel`;
  R16 and RGB565 use their own scales (neither rule applies).
* `GetPixelBilinear` at texel centres filters, so it is not a plain byte read.
* `Texture3D.GetPixel` reads b / 255 like Texture2D's `GetPixel`, its `GetPixels` b * (1 / 255);
  `SetPixel` stores a value half way between two bytes as the upper byte.
* B8G8R8A8 array and volume assets hold B, G, R, A bytes and report BGRA32 (B8G8R8A8_UNorm), and
  `GraphicsFormatUtility.GetTextureFormat` maps both B8G8R8A8 formats to BGRA32.
* `EncodeToEXR` succeeds on a readable imported texture as its first CPU access.
* An imported readable cubemap's `Apply(false)`, as the first access or after `GetPixels`, leaves
  every face the GPU draws unchanged.
* Changing a bound Texture3D's `wrapModeW` takes effect on the next draw: Clamp repeats the edge
  slice beyond the depth, Repeat wraps again, with or without an `Apply` in between.
