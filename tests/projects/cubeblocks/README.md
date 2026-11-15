# Compressed cube blocks

What compressed cube textures give a game's CPU reads and writes (tests/projects/cubecompression
covers what the GPU samples) on the Standalone target (Gamma and Linear projects)
and the Android target (Gamma and Linear projects with the default compression format unset, and
Gamma projects whose Android defaults are ETC2 and ASTC), with the texturecompression probe's six
project profiles.
`make_textures.py` writes the sources: 6:1 horizontal strips with 32-pixel faces, LDR as PNG and
HDR as Radiance `.hdr`. `Assets/Editor/TextureCases.txt` lists the importer cases (one setting per
case: compression quality, convolution, mips, sRGB); `ProbeRunner.Create` imports each source as a
readable `TextureCube` from a full cubemap layout and writes the scene.
`Assets/Scripts/CubeCompressionProbe.cs` records for every import: size, mip count, `format`,
`graphicsFormat`, readability, each face's `GetPixelData<byte>` length per mip, and `GetPixel`
of two texels of the top mip and one of the last mip of every face; then writes (`SetPixel`,
`SetPixels`, `Apply` without and with mips, a level copied through `GetPixelData` views) to two
compressed cubes and an uncompressed one, with what each throws and logs and the texels, format
and level bytes afterwards.

```sh
/usr/bin/python3 tests/projects/cubeblocks/make_textures.py
python3 tests/projects/cubeblocks/run.py create --release 6000.0.71f1          # metas, scene
python3 tests/projects/cubeblocks/run.py editor --release 6000.0.71f1 --label L   # two captures per profile
python3 tests/projects/cubeblocks/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=<unbound> python3 tests/projects/cubeblocks/run.py gate --release 6000.0.71f1 --label L
```

`gate` runs Unbound and compares each profile with `Reference/` exactly; `residuals.json` lists
the known differences (`{profile: {key or glob: why}}`).

## Measured (6000.0.71f1, both repeats identical)

- A cube is compressed in the same format a 2D texture with the same settings gets on that target:
  Standalone DXT1/DXT5/BC7 for LDR (BC7 at high quality, DXT1 for low) and BC6H for HDR;
  Android without a default format ETC_RGB4 in Gamma and ETC2_RGB in Linear (ETC_RGB4 for an
  sRGB-off source in both), ETC2_RGBA8 with alpha; Android ETC2 ETC2_RGB/ETC2_RGBA8; Android
  ASTC 6x6 at normal, 4x4 at high, 8x8 at low quality. An HDR cube on Android uses the LDR
  format of its quality, its uncompressed form is RGB24 (RGBAHalf on Standalone).
- Each face of each mip is a whole number of blocks: a 1x1 or 2x2 mip of a 4x4-block format
  takes one block, `GetPixelData` of a face returns that level's blocks.
- `GetPixel` of a compressed cube decodes the blocks.
- Writes to a compressed cube: `SetPixel` logs `Unsupported GraphicsFormat(<n>) for SetPixel
  operations.` with the linear graphics format of its format whatever the colour space (DXT1 97,
  ETC2_RGB 120) and keeps the texel; `SetPixels` throws `ArgumentException` (`Cubemap.SetPixels:
  texture uses an unsupported format.`); `Apply(false)` is silent; `Apply(true)` logs `Rebuilding
  mipmaps of compressed textures is not supported` and keeps the mips. An uncompressed cube takes
  every write (control).
- A level's blocks copied through `GetPixelData` views (`write.*.view*`) are what `GetPixel` reads,
  before `Apply(false)` and after it; the other faces and levels keep theirs.

## Current results

416 values per profile.

| Profile | Exact values / total | Listed residual values |
|---|---:|---:|
| Gamma | 396 / 416 | 20 |
| Linear | 415 / 416 | 1 |
| AndroidGamma | 342 / 416 | 74 |
| AndroidLinear | 231 / 416 | 185 |
| AndroidETC2 | 221 / 416 | 195 |
| AndroidASTC | 415 / 416 | 1 |

Before cubes were compressed, of the first 368 values (the write controls came later): 116, 132,
102, 112, 113 and 193 exact.
Every size, mip, format, byte-length and message value matches, and so does every Standalone
BC6H texel but a gamma project's last mips. The residuals are texels: ETC blocks of Unbound's own
encoder (within 0.035), the last mip of an HDR cube in the Gamma project (within 0.007, the
uncompressed one already different before), and an uncompressed cube's mips rebuilt by
`Apply(true)` (one 8-bit step of Unbound's box filter, as before).
