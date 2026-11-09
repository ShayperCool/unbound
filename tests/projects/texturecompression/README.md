# Texture compression

What block-compressed textures give a game on the Standalone target, Gamma and Linear projects.
`make_textures.py` writes the sources and the importer cases (one setting per case, the
textureimport probe's images); `ProbeRunner.Create` applies them in the Editor.
`Assets/Scripts/TextureCompressionProbe.cs` records:

- every import: size, mip count, `format`, `graphicsFormat`, readability, `GetRawTextureData`,
  each level's `GetPixelData<byte>` length, `GetPixels32` of every level, `GetPixel(5, 7)` and
  `GetPixelBilinear(0.3, 0.6)` as floats, and the GPU's samples (a blit into an ARGBFloat target
  read back); unreadable textures: what `GetPixels` and `GetPixelData` throw;
- `SystemInfo.SupportsTextureFormat` of every `TextureFormat`, `SystemInfo.IsFormatSupported` of
  the compressed graphics formats, `SystemInfo.copyTextureSupport`;
- compressed `Texture2D`s made by code (also 30x18 and 20x12), `LoadRawTextureData` with an
  import's blocks (whole chain, too short, too long, top level only), `Texture2D.Compress(false)`
  and `(true)` of RGBA32, RGB24, R8, RG16, a 30x18 RGBA32 and a compressed texture, and
  `Graphics.CopyTexture` between compressed textures (whole, a block-aligned region, an unaligned
  region, one mip, from an unreadable texture, into an unreadable texture, into a readable
  import, DXT1 to DXT5, DXT5 to BC7, DXT1 to RGBA32), `Reinitialize` of a compressed texture,
  writes to a compressed import, and explicit RGB24/R8 imports of a coloured, transparent source;
- R8, RG16 and Alpha8 `Texture2D`s made by code, linear and sRGB: format, raw data, pixels and
  the GPU's samples, from `LoadRawTextureData` (with mips) and from `SetPixels32`; `CopyTexture`
  from a linear R8/RG16 texture into an sRGB one, back, and from a render texture of the format.

Each control records what it threw and the errors it logged.

```sh
python3 tests/projects/texturecompression/make_textures.py
python3 tests/projects/texturecompression/run.py create --release 6000.0.71f1          # metas, scene
python3 tests/projects/texturecompression/run.py editor --release 6000.0.71f1 --label L   # two captures per profile
python3 tests/projects/texturecompression/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=<unbound> python3 tests/projects/texturecompression/run.py gate --release 6000.0.71f1 --label L
```

`gate` runs Unbound and compares each profile with `Reference/` exactly; `residuals.json` lists
the known differences (`{profile: {key or glob: why}}`).

## Measured (6000.0.71f1, both repeats identical)

Observations of this release, its Standalone target and the Editor's OpenGL Core device:

- Only 2D textures whose sides are powers of two and multiples of 4 are compressed: 20x12,
  30x18 and 2x2 stay RGBA32 (RGB24 when opaque), automatic or an explicit DXT5; 64x16 compresses.
- Automatic formats: Normal and Low Quality give DXT1, or DXT5 when a texel is not opaque (alpha
  source None: DXT1); High Quality BC7; normal maps DXT5 (Normal, Low) and BC7 (High), DXT5nm
  layout; HDR (EXR) BC6H at every quality; a Single Channel texture's red BC4, its alpha Alpha8.
- Compressor quality (0, 50, 100) does not change DXT1, DXT5 or BC4 blocks; it changes BC7 and
  BC6H.
- `GetRawTextureData` of an unreadable compressed import returns its blocks;
  `GetPixels32` of a level smaller than a block returns the whole decoded block, the level
  repeated as tiles (blocks of 2x2 and 1x1 levels are encoded from the tiled level).
- `GetPixels32` decodes colour with truncating thirds and alpha/red/green steps rounded to
  nearest; BC4 reads (r, 0, 0, 1), BC5 (r, g, 0, 1).
- `new Texture2D(30, 18, DXT1)` throws "Failed to create texture because of invalid parameters."
- R8 and RG16 made by code are R8_UNorm and R8G8_UNorm in the Gamma project, sRGB or not; in the
  Linear project sRGB ones are R8_SRGB and R8G8_SRGB and the GPU decodes their red (and green)
  from sRGB; R8G8_SRGB samples blue 1, R8G8_UNorm blue 0. Alpha8 samples (0, 0, 0, a) either way.
  `CopyTexture` between R8_UNorm and R8_SRGB textures (or an R8 render texture) copies the raw data
  and the GPU's texels; between R8G8_UNorm and R8G8_SRGB it copies the raw data, but the
  destination's GPU texels stay as they were.

## Documented (Unity 6000.0 Scripting API)

- [Texture2D.Compress](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Compress.html):
  DXT1 without alpha, DXT5 with alpha, R8 to BC4, RG16 to BC5; `highQuality` dithers; nothing
  happens for a compressed texture.
- [Graphics.CopyTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Graphics.CopyTexture.html):
  region coordinates and sizes of block formats must be multiples of the block size; CPU data is
  copied when both textures are readable, but not for a region of a compressed texture.
- [Texture2D.LoadRawTextureData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html):
  the data must fill the whole mip chain in the texture's format, else it fails.
- [Texture2D.GetPixels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetPixels.html):
  needs `isReadable`; compressed textures are decompressed.
