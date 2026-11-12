# Texture compression

What block-compressed textures give a game on the Standalone target (Gamma and Linear projects)
and the Android target (Gamma and Linear projects with the default compression format unset, and
Gamma projects whose Android defaults are ETC2 and ASTC).
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

- For BC imports with mipmaps, only 2D textures whose sides are powers of two and multiples of 4 are compressed: 20x12,
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

## Measured on the Android target (6000.0.71f1, both repeats identical)

The same images with an Android override where a case names `android=` (its quality applies):

- Automatic formats at every compression level follow the project's default compression
  format: unset (ETC) gives ETC_RGB4 for opaque textures and ETC2_RGBA8 with transparent texels;
  ETC2 gives ETC2_RGB and ETC2_RGBA8. Normal maps are opaque; a Single Channel texture's red is
  EAC_R and its alpha Alpha8; HDR textures are ETC2_RGBA8.
- ETC_RGB4 needs power-of-two sides even without mips (20x12: RGB24 uncompressed, alpha dropped,
  while ETC2 compresses it); in the Linear project an sRGB-flagged ETC_RGB4 (normal maps too,
  whose flag stays on) is ETC2_RGB, a linear-flagged one stays ETC_RGB4.
- Compressor quality Fast (0), Normal (50) and Best (100) select the project's Fast, Normal and
  Best ETC compressors (EditorSettings; etcpak, ETCPACK Fast and Etc2Comp Best by default), whose
  blocks differ.
- `Texture2D.Compress` gives ETC2_RGBA8 for colour formats with alpha and ETC2_RGB for RGB24. Direct
  R8/RG16 textures give EAC_R/EAC_RG; copied imports named R8/RG16 may actually be RGB24 on this target.
- `SystemInfo.SupportsTextureFormat` is true for ETC and EAC formats, false for ASTC.

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

## Import encoder controls

The additional BC controls cover flat colour/channels, alpha edges, linear colour, normals,
rectangular levels, 20x12 without mips, 30x18 with mips, and qualities 0, 49, 50, 99 and 100.
`block_report.py` prints exact blocks / total for every compressed texture and format, alongside
all raw differences from `compare.py`. A different format, size, mip count or raw-data length
counts as zero matching blocks and is identified explicitly.

| Source, Unity 6000.0 | Contract or observation |
|---|---|
| [TextureImporterPlatformSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporterPlatformSettings.html) | An enabled platform override replaces the default platform parameters. `NoiseBC7Q49` has default quality 100 and an enabled Standalone override of 49; `NoiseHighDisabledOverride` disables the same override and uses default quality 100. Their blocks differ, and the disabled case matches NoiseHighQ100. |
| [SetPlatformTextureSettings](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporter.SetPlatformTextureSettings.html) | Quality 0, 50 and 100 name Fast, Normal and Best. No compressed-block identity is promised. |
| Editor reference, added BC controls | BC7 changes at qualities 50 and 100; BC6H uses its Fast setting at 0, Best at 100 and Normal for intermediate values. These encoder choices are measured for 6000.0.71f1 only. |

The measured encoder mechanism is selected through unbound-compat. Other releases retain the
previous ISPC approximation; they have no exact-block claim. The default BC7 encoder is bc7e;
the alternate ISPC setting remains an approximation. Both encoders' upstream licences allow
source redistribution. No proprietary encoder library is required by an Unbound build.

Measured import block totals, Gamma / Linear, including every mip:

| Format | Exact / total, Gamma | Exact / total, Linear |
|---|---:|---:|
| BC4 | 1677 / 2058 | 1677 / 2058 |
| BC5 | 1047 / 1133 | 1047 / 1133 |
| BC6H | 2371 / 2401 | 2387 / 2401 |
| BC7 | 6218 / 6278 | 6218 / 6278 |

BC7 residuals are in CutoutBC7, FlatBC7 and SoftHigh. The largest level-0 channel difference
is 195/255 in CutoutBC7, and a lower FlatBC7 level reaches 109/255. These remain unresolved.
BC4/BC5 retain differences in endpoint fitting and six-/eight-step selection; BC6H retains
encoding differences and its existing CPU float-read limitation. The comparator lists every
raw discrepancy; residual declarations for these imports name exact keys rather than globs.


## Runtime encoder and Alpha8 storage

The 6000.0.71f1 profile uses a runtime channel quantizer shared by DXT5 alpha, BC4 and BC5,
and a colour fit with rounded thirds. `highQuality` enables colour dithering. The seed controls
put the first red/green component below the rest of its block, so the bounds' first alpha
sample affects the result. Rectangular RGB24/RGBA32 controls fill each mip independently,
including partial blocks. A constant RGB field with changing alpha exercises the colour fit
in both quality modes. These controls extend the existing noise, gradient and channel cases.
Other releases retain the previous approximation, including its missing highQuality dithering.

Alpha8 keeps one CPU byte per texel and samples (0,0,0,a), as the channel controls record.
It still uses RGBA8 GPU storage. [wgpu-types 27.0.1's texture view descriptor](https://docs.rs/wgpu-types/27.0.1/wgpu_types/struct.TextureViewDescriptor.html)
has no component mapping, and its format set has no alpha-only format. An R8 view supplies
red and an alpha of one. Consequently, a one-channel upload cannot preserve Alpha8 sampling
through the current view API; a shader-side component mapping is a separate renderer change.

Measured runtime blocks (Gamma and Linear are identical):

| Format | Fast exact / total | High quality exact / total |
|---|---:|---:|
| BC4 | 347 / 347 | 347 / 347 |
| BC5 | 347 / 347 | 347 / 347 |
| DXT1 | 350 / 353 | 350 / 353 |
| DXT5 | 621 / 628 | 626 / 628 |

Runtime edge blocks repeat their remaining rows and columns. The 3x2 RGB mip still has an
unresolved colour/padding difference, reaching 35/255 in a decoded channel. Other residuals
are small-mip colour fitting and the flat-RGB fast case (GPU difference at most 2/255).
They remain exact-key residuals; no tolerance has changed. The full comparator records
4905/5161 exact values in each Standalone profile, with 256 listed residuals, and
4236, 4223, 4227 and 4205/5161 in AndroidGamma, AndroidLinear, AndroidETC2 and AndroidASTC,
with 925, 938, 934 and 956 residuals.
Android runtime ETC encoding is unchanged by these encoder changes; direct R8/RG16 runtime
EAC storage remains unresolved. The runtime encoder profile is part of the compression key.

For this runtime change, the ordinary-project clean-cache import processed 4397 textures with
zero failures: 16.65 s before and 18.36 s after. Two subsequent runs took 21.44 s and 26.69 s
on the shared machine while other validation jobs were active. Import encoder selection and
quality are unchanged in this part; these wall times do not isolate encoder cost.

## Fast ASTC and Crunch controls

The AndroidASTC profile names ASTC as the default compression format. Automatic selection is
4x4 at High Quality, 8x8 at Low Quality, and 6x6 at Normal; Single Channel red follows those
footprints and alpha stays Alpha8. Normal maps keep XYZ channels with opaque alpha. These are
6000.0.71f1 measurements, including Fast controls outside the explicit-format cases.
The public [TextureImporterCompression](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporterCompression.html)
API describes platform-dependent quality formats; it does not promise compressed-block identity.
[ASTC_6x6](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureFormat.ASTC_6x6.html)
uses 128 bits for each 6x6 footprint. Raw sizes in the probe distinguish that from 4x4 BC blocks.

The measured Fast LDR profile (quality 0, footprints 4x4, 5x5, 6x6 and 8x8) uses the complete
C interface packaged with intel_tex_2. Its [ISPCTextureCompressor source](https://github.com/GameTechDev/ISPCTextureCompressor)
is MIT licensed. Padding clamps the edge texel, including sub-block mips; ASTC GetPixels32
returns actual mip dimensions. Tiny 2x2 imports, 20x12 and 30x18 mipless imports compress; the
30x18 mip chain stays uncompressed. ASTC constructors accept 7x5 sides. Copy controls use distinct source/destination bytes:
matching 5x5 footprints copy the CPU blocks; a 5x5-to-6x6 copy keeps the destination unchanged. Qualities other than
Fast, HDR ASTC, and 10x10/12x12 remain uncompressed approximations with explicit diagnostics;
Single Channel ASTC encoder settings remain unresolved and retain uncompressed storage with a diagnostic;
other releases retain the previous path. GPU storage uses decoded RGBA8 pixels, without a second lossy encoding. Native ASTC upload needs a separate renderer capability path.

The normal-map control also records a Standalone RGB24 override becoming DXT5, while Default
RGB24 imports stay RGB24. The measured import profile applies that fallback before processing.
Texture cache v28 includes these output changes and malformed-block validation.

Crunch controls cover DXT1 and DXT5 at qualities 0, 50 and 100, with ETC_RGB4Crunched and
ETC2_RGBA8Crunched Android counterparts, gradients, noisy alpha and flat colour. Crunch encoding
and archive storage remain unresolved. The [public Crunch fork's ZLIB licence](https://github.com/Unity-Technologies/crunch/blob/unity/license.txt)
permits redistribution; this is an implementation follow-up. The importer keeps uncompressed
pixels and diagnoses that difference. block_report.py counts the missing logical GPU blocks
from dimensions and mip levels, rather than treating archive bytes as blocks. If two archives
of the same format differ, their per-block exactness needs a transcoded comparison; the report
labels its zero verified lower bound instead of asserting every decoded block differs.


ASTC raw-data and copy operations tolerate malformed block payloads. The decoder checks the
[2D mode weight and endpoint budgets](https://github.com/ARM-software/astc-encoder/blob/main/Source/astcenc_block_sizes.cpp)
before reading variable-length fields; impossible budgets produce the format's opaque magenta
error colour. The copy control's source bytes are deliberately different from its destination.
Regression tests also decode deterministic arbitrary payloads at every supported footprint.

Measured ASTC import blocks, including every mip:

| Footprint / quality | AndroidGamma, AndroidLinear, AndroidETC2 | AndroidASTC default profile |
|---|---:|---:|
| 4x4 Fast | 619 / 686 | 1171 / 1372 |
| 5x5 Fast | 241 / 241 | 241 / 241 |
| 6x6 Fast | 716 / 717 | 1201 / 1409 |
| 8x8 Fast | 113 / 114 | 201 / 202 |
| 4x4 Normal / Best | no cases | 0 / 1029 at each quality |
| 6x6 Normal | 0 / 173 | 0 / 2771 |
| 6x6 Best | no cases | 0 / 346 |
| 8x8 Normal | no cases | 0 / 176 |
| 8x8 Best | 0 / 88 | 0 / 88 |
| HDR 6x6 Normal | 0 / 173 | 0 / 173 |

The default profile's Fast 6x6 denominator includes 173 unresolved Single Channel blocks with
no compressed storage. The other 35 residual blocks are gradient fitting and one Flat block;
the ordinary Android profiles have only that Flat block left at 6x6. Fast 4x4 residuals are
opaque gradients; Fast 8x8 has one Noise block. Flat6 reaches 88/255 decoded channel error.
For matching valid blocks the current byte decoder still differs by 1/255 in some channels.
Arbitrary constructor payloads can differ across the full channel range in CPU reads; their
raw blocks and sizes match. These are explicit decoder follow-ups, including the decoder
shared with AssetBundle content, rather than tolerance changes.

Crunch storage is absent for 0/1029 DXT1Crunched and 0/1372 DXT5Crunched logical blocks in each
Standalone profile, and 0/1029 ETC_RGB4Crunched and 0/1372 ETC2_RGBA8Crunched in each Android
profile. Quality-specific counts remain in block_report.py's output. The part's ordinary-project
clean-cache import was 20.58 s before and 21.52 s after, 4397 textures and zero failed imports.
