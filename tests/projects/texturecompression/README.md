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

## Current results

The canonical reference contains 8663 values per profile for Unity 6000.0.71f1. Both independent
GPU Editor recordings match it. The strict gate passes with these explicitly listed residuals;
exact values, exact compressed blocks and visually close values are separate measurements.

| Profile | Exact values / total | Listed residual values |
|---|---:|---:|
| Gamma | 8511 / 8663 | 152 |
| Linear | 8513 / 8663 | 150 |
| AndroidGamma | 7921 / 8663 | 742 |
| AndroidLinear | 7902 / 8663 | 761 |
| AndroidETC2 | 7876 / 8663 | 787 |
| AndroidASTC | 8271 / 8663 | 392 |

Texture cache v41 includes HDR RGBM source preparation and multiplier rounding. NativeApi 66
provides the compressed-texel callback and ASTC/ETC packet formats. The sections below retain
per-format and per-quality block counts, ordinary-content controls and each part's import timings.
Raw discrepancies and comparator tolerances are unchanged.

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
| DXT1 | 5401 / 5401 | 5401 / 5401 |
| DXT5 | 5591 / 5592 | 5591 / 5592 |
| BC4 | 2417 / 2417 | 2417 / 2417 |
| BC5 | 1149 / 1149 | 1149 / 1149 |
| BC6H | 4317 / 4376 | 4349 / 4376 |
| BC7 | 6904 / 6964 | 6904 / 6964 |

The one DXT5 residual is WideNormal's 2x1 mip, whose source filtering differs.
BC7 residuals are in CutoutBC7, FlatBC7 and SoftHigh. The largest level-0 channel difference
is 195/255 in CutoutBC7, and a lower FlatBC7 level reaches 109/255. These remain unresolved.
BC4/BC5 match every current import block. BC6H retains
encoding differences and its existing CPU float-read limitation. The comparator lists every
raw discrepancy; residual declarations for these imports name exact keys rather than globs.


The channel fitter compares six- and eight-step continuous fits before assigning indices to
rounded ramps. It uses nearest-even lattice quantization, four independent error sums and
reciprocal byte normalization. At most two distinct values use their endpoints directly;
other equal-error fits prefer six steps with fixed extremes. ChannelFitsBC4/BC5 cover narrow
fields, fixed extremes and repeated values. Independent channels, flat inputs, rectangular
levels and all mip levels remain covered. Two ordinary UI/effect textures add 2734/2734 exact
blocks for each of BC4 and BC5 outside the synthetic corpus; their private source images stay
outside the repository. BC7 on those same textures is 2732/2734 exact and remains unresolved.

[Single Channel import settings](https://docs.unity3d.com/6000.0/Documentation/Manual/texture-type-singlechannel.html)
describe red/alpha selection and mip generation. The source filtering rule is a measured
6000.0.71f1 observation: compressed automatic red textures filter mips using the authored
sRGB flag, then store a linear channel. HeightRed and explicit HeightBC4 have identical raw
blocks, while HeightRedLinear differs below mip zero. Uncompressed HeightSingleSrgb and
HeightSingleLinear are identical R8 data and filter linearly. These controls separate filtering
from GPU sampling. The existing measured import profile selects the rule for 2D textures;
other releases retain their prior behavior. The channel-fit and red-filter changes first used cache v30; the current format is v36.


The part's 4397-texture clean-cache import had zero failures throughout. Its first pass was
15.95 s before; final after-runs took 23.07 s and 19.68 s on the shared machine. Alternating
same-build-setting runs then took 15.97/20.34 s before and 19.40/25.46 s after. Total process CPU
time, including the cached second pass, was 92.07/92.91 s before and 90.37/91.60 s after.
The wall times overlap the concurrent-work variation; these runs show no increase in process
CPU cost and do not isolate encoder time. Earlier after-runs were 21.04 s and 14.88 s.


## Runtime encoder and Alpha8 storage

The 6000.0.71f1 profile uses a runtime channel quantizer shared by DXT5 alpha, BC4 and BC5,
and a colour fit with rounded thirds. `highQuality` enables colour dithering. The seed controls
put the first red/green component below the rest of its block, so the bounds' first alpha
sample affects the result. Rectangular RGB24/RGBA32 controls fill each mip independently,
including partial blocks. A constant RGB field with changing alpha exercises the colour fit
in both quality modes. These controls extend the existing noise, gradient and channel cases.
Other releases retain the previous approximation, including its missing highQuality dithering.

Alpha8 keeps one CPU byte per texel and samples (0,0,0,a), as the channel controls record.
It still uses RGBA8 GPU storage. [wgpu-types 30.0.1's texture view descriptor](https://docs.rs/wgpu-types/30.0.1/wgpu_types/struct.TextureViewDescriptor.html)
has no component mapping, and its format set has no alpha-only format. An R8 view supplies
red and an alpha of one. Consequently, a one-channel upload cannot preserve Alpha8 sampling
through the current view API; a shader-side component mapping is a separate renderer change.

The current GPU allocation stores four bytes per texel instead of one, an extra three bytes
and four times the unpadded upload traffic. CPU/import storage and the upload packet retain
one byte per texel; the renderer expands it to RGBA8 before writing the GPU texture.

| 1024x1024 Alpha8 texture | One-channel payload | Current RGBA8 GPU payload | Extra GPU payload |
|---|---:|---:|---:|
| Top level only | 1 MiB | 4 MiB | 3 MiB |
| Full mip chain | about 1.33 MiB | about 5.33 MiB | about 4 MiB |

These are texel payload sizes; device allocation alignment can add overhead. The expansion
also needs a CPU conversion and temporary four-byte output. This part records the cost and
does not change renderer storage.

Two ordinary UI/effect Alpha8 images, recorded twice with the GPU Editor in Gamma and
AndroidGamma, match all 37 values per image. Their two complete mip chains contain 43690
one-byte texels per profile, with exact raw bytes and GPU samples. Block counts do not apply
to this uncompressed format. Fresh repeats of the complete six-profile reference are identical
to all 8229 canonical rows. The part's 4397-texture clean-cache runs have zero failures:
20.18 s before and 19.58 s after, total process CPU 94.45 s before and 96.45 s after.
There is no encoder or storage change; shared-machine timings do not isolate Alpha8 cost.

Measured runtime blocks (Gamma and Linear are identical):

| Format | Fast exact / total | High quality exact / total |
|---|---:|---:|
| BC4 | 362 / 363 | 362 / 363 |
| BC5 | 362 / 364 | 362 / 364 |
| DXT1 | 350 / 353 | 350 / 353 |
| DXT5 | 621 / 628 | 626 / 628 |

Runtime edge blocks repeat their remaining rows and columns. The 3x2 RGB mip still has an
unresolved colour/padding difference, reaching 35/255 in a decoded channel. Other residuals
are small-mip colour fitting and the flat-RGB fast case (GPU difference at most 2/255).
BC4 and BC5 now include the later partial-level controls: 2/726 and 4/728 blocks differ
across both qualities, reaching 145/255 in a decoded channel. They remain exact-key residuals;
no tolerance has changed. Current profile-level totals are listed above.
Android runtime ETC encoding is unchanged by these encoder changes; direct R8/RG16 runtime EAC uses the measured profile described below. The runtime encoder profile is part of the compression key.

For this runtime change, the ordinary-project clean-cache import processed 4397 textures with
zero failures: 16.65 s before and 18.36 s after. Two subsequent runs took 21.44 s and 26.69 s
on the shared machine while other validation jobs were active. Import encoder selection and
quality are unchanged in this part; these wall times do not isolate encoder cost.

## ASTC profiles and Crunch controls

The AndroidASTC profile selects ASTC by default. Automatic selection is 4x4 at High Quality,
8x8 at Low Quality, and 6x6 at Normal, including Single Channel red and alpha. Normal maps
keep XYZ channels with opaque alpha. Automatic HDR remains ETC2_RGBA8; explicit ASTC HDR
formats use HDR blocks. These choices are measured for 6000.0.71f1.

| Source, Unity 6000.0 | Contract or observation |
|---|---|
| [Texture compression formats](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TextureImporterOverride.html) | ASTC uses 128 bits per block, with six square footprints from 4x4 through 12x12. HDR support depends on the graphics device and API; unsupported devices require a fallback. The probe checks every footprint, partial edge blocks and sub-block mips. |
| [compressionQuality](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TextureImporter-compressionQuality.html) | Quality ranges from 0 to 100. The API does not promise block identity. NoiseASTC6Q1/Q49/Q50/Q51/Q99/Q100 distinguish the measured search boundaries. |
| Editor reference, added ASTC controls | Standard LDR quality 0 at 4x4/5x5/6x6/8x8 uses ISPC Fast. Other qualities, 10x10/12x12, HDR and Single Channel use Arm astc-encoder 4.3.0, with effort 10 below quality 50, 60 at 50, and 98 above 50. Single red swizzles RRR1; single alpha uses 000A. HDR inputs are quantized to half precision before encoding. |

The complete Fast C interface packaged with intel_tex_2 remains in use for that slice;
[ISPCTextureCompressor](https://github.com/GameTechDev/ISPCTextureCompressor) is MIT licensed.
The other measured profiles use the vendored [Arm astc-encoder 4.3.0](https://github.com/ARM-software/astc-encoder/tree/4.3.0),
under Apache-2.0. Its dependency notes record the context-local settings and shared-table fixes.
Mixed-quality controls verify that earlier textures cannot alter a later encoder's search settings.
The same Compat flag selects the measured ASTC mechanism. Other releases retain diagnosed
uncompressed storage; no exact-block claim applies to them.

Padding clamps the edge texel, including sub-block mips. GetPixels32 returns actual mip dimensions.
Tiny 2x2 and mipless 20x12/30x18 imports compress; non-power-of-two mip chains use RGBA32, including opaque and HDR sources. The fallback
filters HDR radiance before clamping each level to bytes, without HDR gamma conversion.
Constructors accept odd dimensions. Distinct source/destination copy controls verify matching
5x5 footprints copy CPU blocks, while 5x5-to-6x6 leaves the destination unchanged. HDR constructor,
raw-view and mip controls retain values above one and alpha, including divergent writes of red
4 in the first mip and red 6 in another mip. HDR GPU fallbacks retain float radiance with RGBA16F storage; LDR uses RGBA8. Neither path applies a second lossy encoding. Native ASTC upload uses the capability and footprint checks described below.

The CPU decoder uses the same Arm library, with the linear LDR profile even for an sRGB texture.
It tolerates arbitrary raw payloads; invalid blocks produce the codec's error colour. Deterministic
arbitrary-payload tests cover malformed data. HDR reads decode to float; byte reads clamp to bytes.
Per-thread decoder contexts are cached by footprint/profile, and HDR GetPixel writes into caller-owned
storage. The previous byte decoder and its separate mode validator have been removed.

The normal-map control also records a Standalone RGB24 override becoming DXT5, while Default
RGB24 imports stay RGB24. Explicit Single Channel RGBA32 preserves all four source channels.
Explicit colour formats keep their authored sRGB flag. In a Gamma project, a Default 2D HDR
source encodes colour to gamma independently of that flag, then stores a linear HDR format;
HdrRGBAFloat and HdrBC6H distinguish source preparation from compressed decoding.
The ASTC and source-processing changes first used cache v33; the current format is v36.

Crunch controls cover DXT1 and DXT5 at qualities 0, 50 and 100, ETC_RGB4Crunched and
ETC2_RGBA8Crunched Android counterparts, automatic selection, normal maps and non-power-of-two
fallbacks. The pinned public zlib-licensed Crunch fork provides archive encoding and transcoding.
The archive and its GPU blocks are retained separately. The additional Crunch section below
records the implemented slice and the remaining HDR-source limitation.


Measured ASTC import blocks, including every mip:

| Footprint / importer quality | AndroidGamma, AndroidLinear, AndroidETC2 | AndroidASTC default profile |
|---|---:|---:|
| LDR 4x4, 0 | 619 / 686 | 1171 / 1372 |
| LDR 4x4, 50 | no cases | 1715 / 1715 |
| LDR 4x4, 100 | no cases | 1029 / 1029 |
| LDR 5x5, 0, 50 and 100 | 241 / 241 at each quality | 241 / 241 at each quality |
| LDR 6x6, 0 | 1408 / 1409 | 2066 / 2101 |
| LDR 6x6, 1, 49, 51 and 99 | 173 / 173 at each quality | 173 / 173 at each quality |
| LDR 6x6, 50 | 1557 / 1557 | 4847 / 4847 |
| LDR 6x6, 100 | 1211 / 1211 | 1557 / 1557 |
| LDR 8x8, 0 | 113 / 114 | 201 / 202 |
| LDR 8x8, 50 | no cases | 352 / 352 |
| LDR 8x8, 100 | 88 / 88 | 88 / 88 |
| LDR 10x10, 0, 50 and 100 | 73 / 73 at each quality | 73 / 73 at each quality |
| LDR 12x12, 0, 50 and 100 | 53 / 53 at each quality | 53 / 53 at each quality |
| HDR 4x4, 0, 50 and 100 | 343 / 343 at each quality | 343 / 343 at each quality |
| HDR 5x5, 0, 50 and 100 | 241 / 241 at each quality | 241 / 241 at each quality |
| HDR 6x6, 0 and 100 | 173 / 173 at each quality | 173 / 173 at each quality |
| HDR 6x6, 50 | 350 / 350 | 350 / 350 |
| HDR 8x8, 0, 50 and 100 | 88 / 88 at each quality | 88 / 88 at each quality |
| HDR 10x10, 0, 50 and 100 | 73 / 73 at each quality | 73 / 73 at each quality |
| HDR 12x12, 0, 50 and 100 | 53 / 53 at each quality | 53 / 53 at each quality |

Single Channel controls include varying red and alpha, three qualities, an authored sRGB flag,
and both automatic and explicit formats. All their blocks match. HDR includes negative, infinite
and NaN source values, partial edge blocks, and float reads above one. Non-power-of-two Single
Channel and HDR controls verify the RGBA32 fallback separately from compression.

The remaining probe fitting differences are in the existing ISPC Fast slice: opaque gradients,
one Flat6 block and one Noise8 block. Flat6 reaches 88/255 decoded channel error. The previous
byte-decoder differences, including arbitrary constructor payloads, are resolved in this corpus.
Four HDR pixel strings in the Gamma Android profiles and nine in AndroidLinear differ only in
Mono/CoreCLR decimal midpoint formatting. Their numbers parse to identical float32 values;
raw blocks, byte reads and GPU samples match. They remain exact-key residuals without changing
the comparator or its tolerance.

Two ordinary UI/effect images, captured twice on the GPU outside the repository, give:

| LDR footprint | Quality 0 exact / total | Quality 50 exact / total | Quality 100 exact / total |
|---|---:|---:|---:|
| 4x4 | 1855 / 2734 | 2734 / 2734 | 2734 / 2734 |
| 5x5 | 1508 / 1834 | 1834 / 1834 | 1834 / 1834 |
| 6x6 | 1087 / 1314 | 1314 / 1314 | 1314 / 1314 |
| 8x8 | 613 / 688 | 688 / 688 | 688 / 688 |
| 10x10 | 484 / 484 | 484 / 484 | 484 / 484 |
| 12x12 | 348 / 348 | 348 / 348 | 348 / 348 |

The ordinary images' uncompressed source controls match at every mip. These results extend the
measured content; the Fast residuals show why the probe's exact 5x5 result is not a universal claim.
The part's clean-cache import of 4397 ordinary-project textures had zero failures, 19.96 s before
and 18.86 s after, with total process CPU including the cached second pass 92.19 s before and
90.08 s after. An earlier after-run took 17.07 s and 83.51 s CPU. Shared-machine wall time does not isolate encoder cost. This Standalone workload
does not measure the added ASTC searches, whose Normal/Best settings perform more work than
retaining uncompressed pixels.



The ASTC_5x5 region controls distinguish 5-aligned coordinates and sizes from 4-aligned ones.
The source has CPU green/GPU red and the destination CPU yellow/GPU blue before the call.
The 5-aligned region changes only the destination GPU; the 4-aligned region changes nothing.
The destination's CPU blocks stay yellow in both controls.

## Crunch archives, runtime EAC and ASTC uploads

The additional 6000.0.71f1 controls have two identical GPU Editor recordings per profile.
All 7467 earlier reference rows remain byte-identical. The current reference has 8229 rows.
The implementation uses the public [Crunch 1.04 fork at revision 8708900](https://github.com/Unity-Technologies/crunch/tree/8708900eca8ec609d279270e72936258f81ddfb7)
under its zlib licence, with portable compilation and checked archive transcoding.
Texture cache v36 records archive storage and format selection; NativeApi 64 carries the
allocation-free compressed texel decoder and ASTC packet formats.

Default colour textures use perceptual Crunch fitting regardless of their authored sRGB flag;
normal maps disable it. NoiseCrunchLinearNoMips and NoiseCrunchSrgbNoMips have identical archives,
while normal-map controls retain their normal layout. Automatic High Quality BC7 and desktop
HDR BC6H ignore Crunch. ASTC and ETC2 RGB also ignore the flag. ETC1 selected for an sRGB texture
in a Linear project becomes the alpha-capable ETC2 Crunch format. A mipmapped 20x12 explicit
RGB Crunch texture falls back to RGB24 in Gamma, dropping alpha; the Linear ETC2 selection
falls back to RGBA32 and preserves alpha. These are release-scoped observations. Other releases
retain diagnosed uncompressed storage for Crunch selection.

| Source, Unity 6000.0 | Contract or measured control |
|---|---|
| [GetPixels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetPixels.html) | Crunch is unsupported by the float-array API; use GetPixels32. The probe records its exception independently from byte reads. |
| [GetRawTextureData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetRawTextureData.html) and [GetPixelData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.GetPixelData.html) | The generic APIs expose CPU storage. The Crunch controls change a header byte through each view, observe it through the byte-array API, then restore it. Generic raw length is the archive length. GetPixelData reports decoded-block lengths but starts at the archive pointer. Only a safe prefix is sampled in the Editor. |
| [LoadRawTextureData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html) | Complete data fills the texture's mip chain. The added control loads a valid archive into a new texture, applies it and records its changed CPU/GPU pixels for all four Crunch formats. |
| [CommandBuffer.CopyTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.CopyTexture.html) | Whole-mip compressed copies can include sub-block levels; regions use block-aligned coordinates and sizes. Readable source and destination textures also copy CPU data. The added ASTC6 and DXT1 controls copy mip 2 of 12x12 textures after source and destination CPU/GPU values have diverged. Destination CPU blocks and GPU pixels become the source values. |
| Editor reference, Crunch reads | GetPixels32 decodes the archive; individual GetPixel/GetPixelBilinear return white without errors. New Crunch constructors retain their format and have zero archive bytes. |

Archives and transcoded GPU blocks are separate stores. Raw views alias the archive. Unbound
pads the view allocation with zeros where decoded-block lengths extend beyond the actual archive;
those extra bytes are an explicit memory-safety choice, not a measured Editor value. Generic raw
views and byte-array reads retain the actual archive length. Invalid archives are rejected before
transcoding. Partial SetPixelData and CopyTexture involving Crunch remain diagnosed unsupported;
archive variants other than the measured fork, shapes other than 2D and dimensions above the
codec's 4096 limit remain outside this implementation. Android 2D HDR source preparation uses
RGBM bytes, including the measured non-Crunch and size-fallback paths, as detailed below.

Measured import blocks, including all mip levels:

| Profile | DXT1Crunched | DXT5Crunched | ETC_RGB4Crunched | ETC2_RGBA8Crunched |
|---|---:|---:|---:|---:|
| Gamma / Linear | 1715 / 1715 | 2928 / 2928 | none | none |
| AndroidGamma | none | none | 2401 / 2401 | 4903 / 4903 |
| AndroidLinear | none | none | none | 7304 / 7304 |
| AndroidETC2 | none | none | 1029 / 1029 | 4903 / 4903 |
| AndroidASTC | none | none | 1029 / 1029 | 3874 / 3874 |

Every produced Crunch archive in this corpus matches byte for byte, including HDR source controls. Valid archive-load controls add 343/343 blocks per
format, or 686/686 ETC2 RGBA blocks in AndroidLinear. Different archives are compared after
transcoding by `block_report.py`, preserving the strict archive comparison separately.
Two ordinary UI/effect images, three qualities and four codecs add 8202/8202 exact blocks for
each of DXT1Crunched, DXT5Crunched, ETC_RGB4Crunched and ETC2_RGBA8Crunched. Their complete
uncompressed source mip chains also match; private images and repeats stay outside Git.

Android runtime Compress now selects unsigned EAC_R/EAC_RG for direct R8/RG16 textures.
Its independent encoder reuses the existing channel fitter; exact ETC encoding remains outside
scope. It produces 0/46 exact EAC_R blocks and 0/60 EAC_RG blocks across both qualities in this
corpus. Manual EAC storage controls retain 2055/2055 red blocks and 7/7 red/green blocks exactly.
The CPU decoder reads eight-bit base-plus-modifier values; GPU decoding retains eleven-bit
channel values in normalized 16-bit textures, rather than narrowing them to bytes. The complete
2048-value multiplier-zero control retains 45 one-UNorm16-step GPU differences on the measured
OpenGL device. Its raw blocks and all CPU bytes match. Devices without normalized 16-bit storage
use the renderer's existing diagnosed fallback.

The measured Android runtime mechanism checks intermediate mip dimensions while both sides
still span at least three texels. A non-block-aligned level there leaves the source uncompressed:
12x8 with mips is unchanged, while 12x8 without mips and 12x4 with mips compress. This resolves
the older rectangular RGB/RGBA controls as well. Standalone runtime BC4/BC5 partial 3x2/3x1 levels now match. The padding rule and current block totals are recorded below.

ASTC packets retain all six LDR/HDR footprints. The renderer uploads native blocks only when
the enabled device features include that channel's ASTC capability and the base extent fits its
actual footprint. Other sizes retain logical dimensions through decoded RGBA8 or RGBA16F
fallbacks; no second lossy encoding or UV-changing padding is introduced. Footprint/capability
checks cover all 18 packet variants, and an HDR partial-extent test preserves values above one.
The available NVIDIA device lacks ASTC, so the full GPU replay verifies fallbacks. Native ASTC
hardware execution remains a follow-up for an ASTC-capable device.

Whole-mip GPU copies round physical extents to the format\'s actual block footprint; regions
retain block-alignment checks even when a device uses decoded fallback storage. Tests cover
all 18 ASTC packet variants, and the DXT1 control exercises a native compressed sub-block copy
on the available GPU. Retained CPU blocks copy at command execution without a GPU upload;
individual reads decode caller-owned storage and repeated copies allocate no managed memory.
The 20 additive copy rows preserve all earlier 8209 reference rows byte for byte.

Strict comparisons after these changes have 8107/8229 exact values in Gamma and Linear;
AndroidGamma, AndroidLinear, AndroidETC2 and AndroidASTC have 7492, 7473, 7447 and 7842 exact.
The remaining 122, 122, 737, 756, 782 and 387 values are listed individually or by existing
residual declarations. Added ETC2 RGB automatic controls reproduce the corresponding older
normal/quality controls in both implementations, including their existing fitting differences.
No raw discrepancy or comparator tolerance is removed.

The part's clean-cache ordinary-project import processed 4397 textures with zero failures:
19.25 s before and 14.16 s after, total process CPU including the cached second pass 94.90 s
before and 96.61 s after. An earlier after-run took 15.98 s and 94.02 s CPU. Shared-machine wall
time varies; these runs show no noticeable import regression for the existing workload. They
do not measure a project dominated by newly enabled Crunch archives or the larger ASTC searches.

## Remaining ETC/EAC scope and close-out

Non-Crunch ETC uses Unbound's independent approximation. Exact ETC encoders whose source
cannot be redistributed remain outside scope. The following import totals include every mip;
a missing compressed representation counts as zero exact blocks rather than disappearing.

| Import format | AndroidGamma | AndroidLinear | AndroidETC2 | AndroidASTC |
|---|---:|---:|---:|---:|
| ETC_RGB4 | 1258 / 6773 | 22 / 343 | 133 / 686 | 133 / 686 |
| ETC2_RGB | 22 / 1372 | 78 / 7817 | 80 / 8831 | 22 / 1372 |
| ETC2_RGBA8 | 113 / 6263 | 113 / 6263 | 113 / 6263 | 107 / 2744 |
| ETC2_RGBA1 | 0 / 343 | 0 / 343 | 0 / 343 | 0 / 343 |
| EAC_R | 0 / 1715 | 0 / 1715 | 0 / 1715 | 0 / 343 |
| EAC_RG | 0 / 343 | 0 / 343 | 0 / 343 | 0 / 343 |

Runtime ETC2_RGB compression is 53/1029 blocks exact in each quality; ETC2_RGBA8 is 0/603
in each quality, in all four Android profiles. Runtime EAC counts and its CPU/GPU precision
scope are recorded above. Import EAC/ETC2_RGBA1 still use diagnosed uncompressed storage. Native ETC uploads select
enabled device capabilities at upload time. Devices without ETC2 retain the prior BC7 fallback
when BC is available, or decoded RGBA8 otherwise. Native hardware execution remains unverified.

The unresolved work includes BC6H fitting/CPU float reads, BC7 alpha-heavy fitting, runtime
partial-mip colour fitting, the old ISPC Fast ASTC slice, exact distributable ETC/EAC fitting, and EAC's 45 one-step normalized-GPU differences.
Split Alpha Channel, mipless automatic ETC1 at non-power-of-two sizes, native ASTC hardware
execution, macOS compilation and other-release profiles remain outside the measured claim.
DXT1's three-colour fourth entry also differs between the measured OpenGL Editor and wgpu.
Alpha8 GPU storage cost is documented above; no renderer redesign is included.

For close-out, another pair of GPU Editor captures for every profile matches all 8229 reference
rows. The two ordinary Alpha8 controls again match every recorded value; the earlier ordinary
BC, ASTC and Crunch results retain their per-format counts above. This documentation-only part
changes no encoder, oracle, residual declaration or tolerance. Its 4397-texture clean-cache
imports have zero failures, 18.85 s before and 17.63 s after, total process CPU 96.44 s before
and 93.08 s after on the shared machine.


## Runtime partial rows, Q404

For the measured 6000.0.71f1 runtime encoder, a partial three-texel row fills its fourth slot
with the middle texel. One- and two-texel rows tile; partial rows repeat vertically. Importer
padding is unchanged. The existing rectangular 3x2/3x1 mips and additional mipless 7x3 R8 and
15x7 RG16 controls check rejection. Additional 24x4 R8 and 24x8 RG16 mip chains reach
compressed three-column levels and distinguish the padding rule from modulo-three tiling. Both
quality modes are recorded twice on the GPU. Every prior 8229 reference value is unchanged;
200 values per profile are additive. Fourteen formerly differing values now match in each Standalone colour space.
Android controls retain the measured intermediate-dimension rejection rule. The two additional
mip chains compress to EAC on Android, with 28 new exact-key fitting residuals per profile;
their raw, CPU and GPU discrepancies remain visible.

No import encoder changes in this part. BC7 retains 6904/6964 exact blocks, BC6H 2712/2744
in Gamma and 2729/2744 in Linear. These fitting residuals are unresolved, including BC7's
large alpha-heavy errors; they are not classified as invisible ties. The existing Fast ASTC
and non-Crunch ETC/EAC tables retain every differing block. Two ordinary images again match
2734/2734 BC4 blocks, 2734/2734 BC5 blocks and 2732/2734 BC7 blocks, with identical source mips.

Clean-cache imports process 4397 textures with zero failures. Initial first passes take
33.67 s before and 20.03 s after; a subsequent pair takes 14.97 s before and 23.34 s after.
Total process CPU, including the cached second pass, is 89.53/97.69 s for the initial pair
and 94.93/99.81 s for the subsequent pair. Shared-machine wall time varies across these runs;
this runtime-only change leaves the import encoders unchanged.

Current runtime blocks per quality are BC4 376/376, BC5 383/383 and DXT1 351/353.
DXT5 remains 621/628 at Fast and 626/628 at High. Across both qualities, unsigned EAC red
is 0/46 and red/green 0/60. The strict results are 8321/8429 in Gamma and Linear, 7664/8429
in AndroidGamma, 7645/8429 in AndroidLinear, 7619/8429 in AndroidETC2 and 8014/8429 in
AndroidASTC. All 108/108/765/784/810/415 residual values remain declared and printed.

## HDR RGBM and ETC uploads, Q404

Nine added HDR controls distinguish quality 0/100, a linear source flag, no mips, discarded
source alpha, nonfinite radiance, compression-independent size fallback and multiplier rounding.
Each has two identical GPU Editor recordings in all six profiles. All earlier 8429 values
remain byte-identical; the reference now has 8663 values per profile.

The measured Android Default 2D colour path filters float radiance, then converts each mip to
RGBM before ETC2 RGBA encoding. A linear-flagged source applies gamma decoding before RGBM.
The shared multiplier uses range 5 and gamma 2.2, rounds upward to a byte step, and divides by
255 before normalizing RGB. This division matters at half-channel boundaries: HdrMultiplier
records RGBM (255,128,48,51) from radiance (1,0.5,0.1875). Source alpha is replaced, and radiance
has no upper clamp before encoding. Nonfinite values are tolerated; HdrSpecialCrunch records
the resulting archive and pixels. These rules reuse the measured Compat profile. Other
releases retain their previous diagnosed HDR fallback.

A size ineligible for block storage retains RGBM RGBA32 on Android. Standalone's automatic
BC6H size fallback filters radiance before narrowing to linear RGB24 and discards alpha.
HdrNpotNormal and HdrNpotCrunch have identical raw data in every profile, so these conversions
do not depend on the Crunch flag. The 3x3 multiplier control also checks both byte fallbacks.

Two ordinary UI/effect sources converted to 128x128 HDR add three Crunch qualities per image
in AndroidGamma and AndroidLinear. Each profile has two identical GPU Editor captures.
Their float source controls match at every mip. The corrected source conversion is checked
against all six archives and their 8202 logical ETC2 RGBA blocks per profile; private images,
repeat recordings and run outputs remain outside Git.

ETC1/ETC2 RGB and ETC2 RGBA packets retain native blocks until the render device is available.
An enabled ETC2 feature and a base extent of whole 4x4 blocks permit native upload. Imported
packets also retain their existing BC7 mip chain for devices without ETC2; runtime packets
retain decoded RGBA fallback behavior. Unsupported or partial extents keep logical dimensions.
Tests distinguish native ETC bytes, a deliberately different retained BC7 payload and decoded
RGBA, in colour/data variants. The available NVIDIA device lacks ETC2, so GPU captures verify
fallbacks; executing native ETC on supported hardware remains a follow-up.

EAC keeps eleven-bit channel precision through normalized 16-bit GPU storage. Its exhaustive
2048-value control still counts 45 one-UNorm16-step differences. Raw blocks and CPU bytes match;
no tolerance or oracle changed, and these last-step differences were not chased.

The part's clean-cache import processes 4397 ordinary-project textures with zero failures.
First-pass time is 22.52 s before and 19.87 s after; total process CPU including the cached
second pass is 97.49/101.73 s. An earlier after-run took 15.64 s and 93.73 s CPU. These
shared-machine runs show no noticeable wall-time regression and do not isolate encoder cost.
The Standalone workload does not measure newly enabled Android HDR compression cost.
